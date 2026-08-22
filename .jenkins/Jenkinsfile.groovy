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

pipeline {
    agent none
    parameters {
        choice(
            name: 'DOCKER_NAMESPACE',
            choices: ['faulo', 'tmp'],
            description: 'Docker image namespace to test'
        )
    }
    options {
        disableConcurrentBuilds()
        disableResume()
        disableRestartFromStage()
    }
    stages {
        stage('Integration Tests') {
            matrix {
                axes {
                    axis {
                        name 'HOST'
                        values 'Dende', 'Garl'
                    }
                    axis {
                        name 'GODOT_VERSION'
                        values '4.0', '4.1', '4.2', '4.3', '4.4', '4.5', '4.6'
                    }
                }
                agent {
                    label "$HOST"
                }
                stages {
                    stage("$HOST: Godot v$GODOT_VERSION") {
                        steps {
                            script {
                                withEnvFile {
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
