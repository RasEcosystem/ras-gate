import { sleep } from 'k6';
import {
    executeRac,
    nonNegativeNumberEnvironment,
    positiveIntegerEnvironment,
    stringEnvironment,
    waitForRasGate
} from './lib/rasgate.js';

const vus =
    positiveIntegerEnvironment('TEST_VUS', 4);

const duration =
    stringEnvironment('TEST_DURATION', '2m');

const pauseSeconds =
    nonNegativeNumberEnvironment(
        'TEST_PAUSE_SECONDS',
        0.2);

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
        checks: ['rate>0.99'],
        http_req_failed: ['rate<0.01'],
        'http_req_duration{name:POST /rac/execute}': [
            'p(95)<5000'
        ],
        rasgate_execution_errors: ['rate<0.01']
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
