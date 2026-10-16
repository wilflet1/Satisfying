plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "dev.pclink"
    compileSdk = 35

    defaultConfig {
        applicationId = "dev.pclink"
        minSdk = 30
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // Sideloaded personal app: sign release builds with the debug key
            // so `assembleRelease` produces an installable APK out of the box.
            signingConfig = signingConfigs.getByName("debug")
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
