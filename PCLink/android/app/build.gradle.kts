plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// CI sets PCLINK_BUILD to its run number; the self-updater compares it.
val buildNumber = (System.getenv("PCLINK_BUILD") ?: "1").toInt()

android {
    namespace = "dev.pclink"
    compileSdk = 35

    defaultConfig {
        applicationId = "dev.pclink"
        minSdk = 30
        targetSdk = 35
        versionCode = buildNumber
        versionName = "0.2.$buildNumber"
    }

    // Every build must be signed with the same key or Android refuses the
    // update. The committed key keeps updates working out of the box; set
    // the PCLINK_KEYSTORE* variables (CI: repository secrets) to use your own.
    signingConfigs {
        create("pclink") {
            storeFile = file(System.getenv("PCLINK_KEYSTORE") ?: "../signing/pclink.jks")
            storePassword = System.getenv("PCLINK_KEYSTORE_PASSWORD") ?: "pclink-sideload"
            keyAlias = System.getenv("PCLINK_KEY_ALIAS") ?: "pclink"
            keyPassword = System.getenv("PCLINK_KEY_PASSWORD") ?: "pclink-sideload"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("pclink")
        }
        debug {
            signingConfig = signingConfigs.getByName("pclink")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

// No AndroidX or third-party libraries: the app uses only the Android
// framework and the Kotlin standard library.
dependencies {}
