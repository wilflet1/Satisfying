//! The agent's long-lived self-signed TLS identity. The phone pins the
//! SHA-256 fingerprint of this certificate when it pairs.

use anyhow::{Context, Result};
use rustls::pki_types::{CertificateDer, PrivateKeyDer, PrivatePkcs8KeyDer};
use std::sync::Arc;

pub struct Identity {
    pub cert_der: Vec<u8>,
    pub key_der: Vec<u8>,
    /// Lowercase hex SHA-256 of the DER certificate.
    pub fingerprint: String,
}

impl Identity {
    pub fn load_or_create(dir: &std::path::Path) -> Result<Identity> {
        let cert_path = dir.join("cert.der");
        let key_path = dir.join("key.der");
        let (cert_der, key_der) = if cert_path.exists() && key_path.exists() {
            (std::fs::read(&cert_path)?, std::fs::read(&key_path)?)
        } else {
            std::fs::create_dir_all(dir)?;
            let ck = rcgen::generate_simple_self_signed(vec!["pclink".to_string()])
                .context("generating TLS certificate")?;
            let cert = ck.cert.der().to_vec();
            let key = ck.key_pair.serialize_der();
            crate::config::write_private(&key_path, &key)?;
            std::fs::write(&cert_path, &cert)?;
            (cert, key)
        };
        let fingerprint = crate::util::sha256_hex(&cert_der);
        Ok(Identity { cert_der, key_der, fingerprint })
    }

    pub fn server_config(&self) -> Result<Arc<rustls::ServerConfig>> {
        let provider = Arc::new(rustls::crypto::ring::default_provider());
        let cfg = rustls::ServerConfig::builder_with_provider(provider)
            .with_safe_default_protocol_versions()?
            .with_no_client_auth()
            .with_single_cert(
                vec![CertificateDer::from(self.cert_der.clone())],
                PrivateKeyDer::Pkcs8(PrivatePkcs8KeyDer::from(self.key_der.clone())),
            )?;
        Ok(Arc::new(cfg))
    }
}

/// Client-side verifier that accepts exactly one certificate, identified by
/// its SHA-256 fingerprint. Used by tests and any Rust client.
#[derive(Debug)]
pub struct PinnedVerifier {
    pub fingerprint: String,
    pub provider: Arc<rustls::crypto::CryptoProvider>,
}

impl rustls::client::danger::ServerCertVerifier for PinnedVerifier {
    fn verify_server_cert(
        &self,
        end_entity: &CertificateDer<'_>,
        _intermediates: &[CertificateDer<'_>],
        _server_name: &rustls::pki_types::ServerName<'_>,
        _ocsp: &[u8],
        _now: rustls::pki_types::UnixTime,
    ) -> Result<rustls::client::danger::ServerCertVerified, rustls::Error> {
        let fp = crate::util::sha256_hex(end_entity.as_ref());
        if crate::util::ct_eq(fp.as_bytes(), self.fingerprint.as_bytes()) {
            Ok(rustls::client::danger::ServerCertVerified::assertion())
        } else {
            Err(rustls::Error::General("certificate fingerprint mismatch".into()))
        }
    }

    fn verify_tls12_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &rustls::DigitallySignedStruct,
    ) -> Result<rustls::client::danger::HandshakeSignatureValid, rustls::Error> {
        rustls::crypto::verify_tls12_signature(
            message,
            cert,
            dss,
            &self.provider.signature_verification_algorithms,
        )
    }

    fn verify_tls13_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &rustls::DigitallySignedStruct,
    ) -> Result<rustls::client::danger::HandshakeSignatureValid, rustls::Error> {
        rustls::crypto::verify_tls13_signature(
            message,
            cert,
            dss,
            &self.provider.signature_verification_algorithms,
        )
    }

    fn supported_verify_schemes(&self) -> Vec<rustls::SignatureScheme> {
        self.provider
            .signature_verification_algorithms
            .supported_schemes()
    }
}

pub fn pinned_client_config(fingerprint: &str) -> Arc<rustls::ClientConfig> {
    let provider = Arc::new(rustls::crypto::ring::default_provider());
    let verifier = Arc::new(PinnedVerifier {
        fingerprint: fingerprint.to_string(),
        provider: provider.clone(),
    });
    let cfg = rustls::ClientConfig::builder_with_provider(provider)
        .with_safe_default_protocol_versions()
        .expect("protocol versions")
        .dangerous()
        .with_custom_certificate_verifier(verifier)
        .with_no_client_auth();
    Arc::new(cfg)
}
