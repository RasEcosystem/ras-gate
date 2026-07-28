import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';

export const capacityRejections =
    new Counter('rasgate_capacity_rejections');

export const executionErrors =
    new Rate('rasgate_execution_errors');

const baseUrl =
    (__ENV.BASE_URL || 'http://ras-gate:8080')
        .replace(/\/+$/, '');

const apiKey = __ENV.API_KEY || '';
const requestTimeout =
    __ENV.REQUEST_TIMEOUT || '35s';

const executePayload = JSON.stringify({
    arguments: readRacArguments()
});

const executeRequestParameters = {
    headers: {
        'Content-Type': 'application/json',
        'X-Api-Key': apiKey
    },
    tags: {
        name: 'POST /rac/execute'
    },
    timeout: requestTimeout
};

export function positiveIntegerEnvironment(
    name,
    fallback)
{
    const rawValue = __ENV[name];

    if (rawValue === undefined || rawValue === '')
        return fallback;

    const value = Number(rawValue);

    if (!Number.isInteger(value) || value <= 0)
        throw new Error(
            `${name} must be a positive integer.`);

    return value;
}

export function nonNegativeNumberEnvironment(
    name,
    fallback)
{
    const rawValue = __ENV[name];

    if (rawValue === undefined || rawValue === '')
        return fallback;

    const value = Number(rawValue);

    if (!Number.isFinite(value) || value < 0)
        throw new Error(
            `${name} must be a non-negative number.`);

    return value;
}

export function stringEnvironment(
    name,
    fallback)
{
    const value = __ENV[name];

    return value === undefined || value === ''
        ? fallback
        : value;
}

export function waitForRasGate()
{
    if (apiKey === '')
        fail('API_KEY must be configured.');

    const attempts =
        positiveIntegerEnvironment(
            'STARTUP_ATTEMPTS',
            30);

    const delaySeconds =
        nonNegativeNumberEnvironment(
            'STARTUP_DELAY_SECONDS',
            1);

    for (let attempt = 1; attempt <= attempts; attempt++)
    {
        const response = http.get(
            `${baseUrl}/rasgate/status`,
            {
                tags: {
                    name: 'GET /rasgate/status (startup)'
                },
                timeout: requestTimeout
            });

        const body = parseJson(response);

        if (response.status === 200 &&
            body !== null &&
            body.success === true &&
            hasValue(body.data))
            return body.data;

        if (attempt < attempts)
            sleep(delaySeconds);
    }

    fail(
        `RasGate did not become ready after ${attempts} attempts.`);
}

export function checkRasGateStatus()
{
    const response = http.get(
        `${baseUrl}/rasgate/status`,
        {
            tags: {
                name: 'GET /rasgate/status'
            },
            timeout: requestTimeout
        });

    const body = parseJson(response);

    return check(response, {
        'RasGate status is 200': () =>
            response.status === 200,

        'RasGate status has a successful envelope': () =>
            body !== null &&
            body.success === true &&
            hasValue(body.data),

        'RasGate status contains an instance name': () =>
            body !== null &&
            hasValue(body.data) &&
            typeof body.data.instanceName === 'string' &&
            body.data.instanceName.length > 0,

        'RasGate status contains a version': () =>
            body !== null &&
            hasValue(body.data) &&
            typeof body.data.version === 'string' &&
            body.data.version.length > 0
    });
}

export function checkRacStatus()
{
    const response = http.get(
        `${baseUrl}/rac/status`,
        {
            tags: {
                name: 'GET /rac/status'
            },
            timeout: requestTimeout
        });

    const body = parseJson(response);

    return check(response, {
        'RAC status is 200': () =>
            response.status === 200,

        'RAC status has a successful envelope': () =>
            body !== null &&
            body.success === true &&
            hasValue(body.data),

        'RAC executable is available': () =>
            body !== null &&
            hasValue(body.data) &&
            body.data.available === true,

        'RAC status contains a version': () =>
            body !== null &&
            hasValue(body.data) &&
            typeof body.data.version === 'string' &&
            body.data.version.length > 0
    });
}

export function executeRac(
    acceptCapacityRejection = false)
{
    const response = http.post(
        `${baseUrl}/rac/execute`,
        executePayload,
        executeRequestParameters);

    const body = parseJson(response);

    const completed =
        response.status === 200 &&
        body !== null &&
        body.success === true &&
        hasValue(body.data) &&
        body.data.exitCode === 0 &&
        body.data.timedOut === false &&
        typeof body.data.standardOutput === 'string' &&
        typeof body.data.standardError === 'string';

    const capacityRejected =
        response.status === 429 &&
        body !== null &&
        body.success === false &&
        hasValue(body.error) &&
        body.error.code === 'rac_capacity_exceeded';

    if (capacityRejected)
        capacityRejections.add(1);

    const expected =
        completed ||
        (acceptCapacityRejection && capacityRejected);

    executionErrors.add(!expected);

    check(response, {
        'RAC execution returned an expected response': () =>
            expected
    });

    return {
        capacityRejected,
        completed,
        response
    };
}

function parseJson(response)
{
    try
    {
        return response.json();
    }
    catch
    {
        return null;
    }
}

function hasValue(value)
{
    return value !== null &&
        value !== undefined;
}

function readRacArguments()
{
    const rawValue = __ENV.RAC_ARGUMENTS_JSON;

    if (rawValue === undefined || rawValue === '')
        return ['--version'];

    let value;

    try
    {
        value = JSON.parse(rawValue);
    }
    catch
    {
        throw new Error(
            'RAC_ARGUMENTS_JSON must contain valid JSON.');
    }

    if (!Array.isArray(value) ||
        value.length === 0 ||
        value.some(argument =>
            typeof argument !== 'string'))
        throw new Error(
            'RAC_ARGUMENTS_JSON must be a non-empty array of strings.');

    return value;
}
