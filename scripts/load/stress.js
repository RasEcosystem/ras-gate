import http from 'k6/http';
import {
    executeRac,
    waitForRasGate
} from './lib/rasgate.js';

http.setResponseCallback(
    http.expectedStatuses(200, 429));

export const options = {
    scenarios: {
        racExecution: {
            executor: 'ramping-vus',
            startVUs: 0,
            stages: [
                {
                    duration: '30s',
                    target: 4
                },
                {
                    duration: '1m',
                    target: 4
                },
                {
                    duration: '30s',
                    target: 8
                },
                {
                    duration: '1m',
                    target: 8
                },
                {
                    duration: '30s',
                    target: 16
                },
                {
                    duration: '1m',
                    target: 16
                },
                {
                    duration: '30s',
                    target: 0
                }
            ],
            gracefulStop: '35s'
        }
    },
    thresholds: {
        checks: ['rate>0.99'],
        http_req_failed: ['rate<0.01'],
        'http_req_duration{name:POST /rac/execute}': [
            'p(95)<35000'
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
    executeRac(true);
}
