// Starts the simulation worker and posts requests to it. Each request gets one JSON string back.
// The worker runs Ssp.Core in a second .NET runtime, so a long render does not block the page.
let worker;
let next = 0;
const pending = new Map();

function start() {
    worker = new Worker(new URL('./simulation-worker.js', import.meta.url), { type: 'module' });
    worker.onmessage = ({ data }) => {
        const request = pending.get(data.id);
        if (data.progress !== undefined) {
            request.progress?.invokeMethodAsync('Report', data.progress);
            return;
        }
        pending.delete(data.id);
        if (data.error === undefined) {
            request.resolve(data.json);
        } else {
            request.reject(new Error(data.error));
        }
    };
    worker.onerror = event => {
        for (const request of pending.values()) {
            request.reject(new Error(event.message));
        }
        pending.clear();
    };
    // NOTE: The import map of the page gives the fingerprinted name of dotnet.js. A worker has no import map.
    worker.postMessage({ dotnet: import.meta.resolve('../_framework/dotnet.js') });
}

function call(method, args, transfer = [], progress = null) {
    if (!worker) {
        start();
    }
    const id = next++;
    return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject, progress });
        worker.postMessage({ id, method, args }, transfer);
    });
}

// Stops the worker at once and rejects the requests that wait. The next call starts a new worker.
export function cancel() {
    worker?.terminate();
    worker = undefined;
    for (const request of pending.values()) {
        request.reject(new Error('Cancelled.'));
    }
    pending.clear();
}

export function convolve(input, ir) {
    const samples = Float64Array.from(input);
    const response = Float64Array.from(ir);
    return call('Convolve', [samples, response], [samples.buffer, response.buffer]);
}

// NOTE: progress is a .NET object with a Report(seconds) method, or null.
export function render(netlist, input, sampleRate, oversample, progress = null) {
    // NOTE: The copy moves to the worker without a second copy, and the caller keeps its input.
    const samples = Float64Array.from(input);
    return call('Render', [netlist, samples, sampleRate, oversample], [samples.buffer], progress);
}

export function versions() {
    return call('Versions', []);
}

export function monitorStart(netlist, sampleRate) {
    return call('MonitorStart', [netlist, sampleRate]);
}

export function monitorProcess(chunk) {
    const samples = Float64Array.from(chunk);
    return call('MonitorProcess', [samples], [samples.buffer]);
}

export function monitorStop() {
    return call('MonitorStop', []);
}
