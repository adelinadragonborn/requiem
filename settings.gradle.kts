include("Java:SkyProc", "Java:Reqtificator")
rootProject.name = "Requiem-Development"
pluginManagement {
    plugins {
        kotlin("jvm") version "1.4.0"
        id("org.jlleitschuh.gradle.ktlint") version "9.3.0"
        id("org.beryx.jlink") version "2.21.3"
    }
}
