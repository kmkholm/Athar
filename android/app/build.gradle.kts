// Athar (أثر) — blockchain transaction tracing, native Kotlin + Jetpack Compose. Reads public explorer APIs only.
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "app.athar"
    compileSdk = 36

    defaultConfig {
        applicationId = "app.athar"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    packaging {
        resources { excludes += setOf("META-INF/*.kotlin_module", "META-INF/versions/**", "META-INF/DEPENDENCIES", "META-INF/LICENSE*", "META-INF/NOTICE*", "META-INF/INDEX.LIST") }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlin { compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17) } }
    buildFeatures { compose = true }
    testOptions { unitTests.isReturnDefaultValues = true; unitTests.all { it.systemProperty("athar.live", System.getProperty("athar.live") ?: "0") } }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.activity.compose)
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.graphics)
    implementation(libs.compose.foundation)
    implementation(libs.compose.material3)
    implementation(libs.compose.icons)
    implementation(libs.lifecycle.runtime.compose)
    implementation(libs.work.runtime)
    implementation(libs.anthropic.java)
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20231013")
}
