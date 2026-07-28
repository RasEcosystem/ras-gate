import {
    checkRacStatus,
    checkRasGateStatus,
    executeRac,
    waitForRasGate
} from './lib/rasgate.js';

export const options = {
    vus: 1,
    iterations: 1,
    thresholds: {
        checks: ['rate==1'],
        http_req_failed: ['rate==0'],
        http_req_duration: ['p(95)<35000'],
        rasgate_execution_errors: ['rate==0']
    }
};

export function setup()
{
    return waitForRasGate();
}

export default function()
{
    checkRasGateStatus();
    checkRacStatus();
    executeRac();
}
