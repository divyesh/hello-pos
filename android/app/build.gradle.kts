import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.ksp)
    alias(libs.plugins.hilt)
    alias(libs.plugins.android.junit5)
    alias(libs.plugins.openapi.generator)
}

// Shared contract (Constitution, Principle V): the Retrofit client is generated from it on every build.
val contractsDir = rootProject.layout.projectDirectory.dir("../contracts")
val generatedApiDir = layout.buildDirectory.dir("generated/openapi")

// API address per developer machine (FR-011): Gradle property or local.properties, emulator default.
val localProperties = Properties().apply {
    val file = rootProject.file("local.properties")
    if (file.exists()) file.inputStream().use(::load)
}
val apiBaseUrl: String = providers.gradleProperty("hellopos.apiBaseUrl").orNull
    ?: localProperties.getProperty("hellopos.apiBaseUrl")
    ?: "http://10.0.2.2:5080/"

android {
    namespace = "com.recbook.hellopos"
    // Compose 1.12 (BOM 2026.09) requires compiling against API 37; runtime behavior follows targetSdk.
    compileSdk = 37

    defaultConfig {
        applicationId = "com.recbook.hellopos"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0-dev"
        buildConfigField("String", "API_BASE_URL", "\"$apiBaseUrl\"")
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }
}

openApiGenerate {
    generatorName.set("kotlin")
    library.set("jvm-retrofit2")
    inputSpec.set(contractsDir.file("openapi.yaml").asFile.absolutePath)
    outputDir.set(generatedApiDir.get().asFile.absolutePath)
    packageName.set("com.recbook.hellopos.data.remote.generated")
    configOptions.set(
        mapOf(
            "serializationLibrary" to "kotlinx_serialization",
            "useCoroutines" to "true",
            "dateLibrary" to "string",
            "omitGradleWrapper" to "true",
            "omitGradlePluginVersions" to "true",
        ),
    )
    ignoreFileOverride.set(layout.projectDirectory.file("openapi-generator-ignore").asFile.absolutePath)
    cleanupOutput.set(true)
    generateApiTests.set(false)
    generateModelTests.set(false)
    generateApiDocumentation.set(false)
    generateModelDocumentation.set(false)
}

androidComponents {
    onVariants { variant ->
        variant.sources.kotlin?.addStaticSourceDirectory(
            generatedApiDir.get().dir("src/main/kotlin").asFile.absolutePath,
        )
    }
}

tasks.named("preBuild") { dependsOn(tasks.named("openApiGenerate")) }

tasks.withType<Test>().configureEach {
    // Lets JVM tests read the shared contracts (pricing cases, OpenAPI) from the repository.
    systemProperty("hellopos.contractsDir", contractsDir.asFile.absolutePath)
    // Declared as an input so any change to contracts/ re-runs the tests instead of reusing old results.
    inputs.dir(contractsDir).withPathSensitivity(PathSensitivity.RELATIVE).withPropertyName("contracts")
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    debugImplementation(libs.androidx.compose.ui.tooling)

    implementation(libs.hilt.android)
    implementation(libs.androidx.hilt.lifecycle.viewmodel.compose)
    implementation(libs.androidx.navigation.compose)
    ksp(libs.hilt.compiler)

    implementation(libs.retrofit)
    implementation(libs.retrofit.kotlinx.serialization)
    implementation(libs.okhttp)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.kotlinx.coroutines.android)

    testImplementation(platform(libs.junit.bom))
    testImplementation(libs.junit.jupiter.api)
    testRuntimeOnly(libs.junit.jupiter.engine)
    testRuntimeOnly(libs.junit.platform.launcher)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.turbine)
    testImplementation(libs.okhttp.mockwebserver)
    testImplementation(libs.konsist)
    testImplementation(libs.json.schema.validator)
}
