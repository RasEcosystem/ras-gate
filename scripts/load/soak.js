import { sleep } from 'k6';
import {
    executeRac,
    nonNegativeNumberEnvironment,
    positiveIntegerEnvironment,
    stringEnvironment,
    waitForRasGate
} from './lib/rasgate.js';

const vus =
    positiveIntegerEnvironment('TEST_VUS', 2);

const duration =
    stringEnvironment('TEST_DURATION', '30m');

const pauseSeconds =
    nonNegativeNumberEnvironment(
        'TEST_PAUSE_SECONDS',
        1);

export const options = {
    scenarios: {
        racExecution: {
            executor: 'constant-vus',
            vus,
            duration,
            gracefulStop: '35s'
        }
    },
    thresholds: {
        checks: ['rate>0.999'],
        http_req_failed: ['rate<0.001'],
        'http_req_duration{name:POST /rac/execute}': [
            'p(99)<10000'
        ],
        rasgate_execution_errors: ['rate<0.001']
    }
};

export function setup()
{
    return waitForRasGate();
}

export default function()
{
    executeRac();
    sleep(pauseSeconds);
}
