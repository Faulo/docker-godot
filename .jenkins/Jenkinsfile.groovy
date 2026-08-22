import org.jenkinsci.plugins.workflow.steps.FlowInterruptedException

def assertValue(actual, expected, description) {
    if (actual != expected) {
        error "${description}: expected '${expected}', got '${actual}'"
    }
}

def candidateImage() {
    return "$DOCKER_NAMESPACE/$DOCKER_IMAGE"
}

def testEmptyProjectImport() {
    docker.image(candidateImage()).inside() {
        def setupExitCode = execStatus 'godot --version'
        assertValue(setupExitCode, 0, 'Godot setup must succeed')

        dir('empty-project') {
            deleteDir()
            catchError(
                message: 'Empty project import test failed',
                stageResult: 'FAILURE',
                buildResult: 'FAILURE'
            ) {
                try {
                    timeout(time: 10, unit: 'SECONDS') {
                        def exitCode = execStatus 'godot --headless --verbose --quit --editor --import'
                        assertValue(exitCode == 0, false, 'Import on an empty project must not return success')
                    }
                } catch (FlowInterruptedException ignored) {
                    error 'Import on an empty project timed out'
                }
            }
        }
    }
}

properties([
    parameters([
        choice(
            name: 'DOCKER_NAMESPACE',
            choices: ['faulo', 'tmp'],
            description: 'Docker image namespace to test'
        )
    ]),
    disableConcurrentBuilds(),
    disableResume()
])

def hosts = ['Dende', 'Garl']
def godotVersions = ['4.0', '4.1', '4.2', '4.3', '4.4', '4.5', '4.6', '4.7']
def dockerNamespace = params.DOCKER_NAMESPACE ?: 'faulo'

stage('Integration Tests') {
    for (def host in hosts) {
        stage("Host: ${host}") {
            node(host) {
                deleteDir()
                checkout scm

                for (def godotVersion in godotVersions) {
                    stage("Godot v${godotVersion}") {
                        catchError(
                            message: "Godot ${godotVersion} integration test failed on ${host}",
                            stageResult: 'FAILURE',
                            buildResult: 'FAILURE',
                            catchInterruptions: false
                        ) {
                            withEnv([
                                "DOCKER_NAMESPACE=${dockerNamespace}",
                                "GODOT_VERSION=${godotVersion}"
                            ]) {
                                withEnvFile {
                                    echo "Testing ${candidateImage()} with Godot ${godotVersion} on ${host}"
                                    testEmptyProjectImport()
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
