// Starts the simulation worker and posts requests to it. Each request gets one JSON string back.
// The worker runs Ssp.Core in a second .NET runtime, so a long render does not block the page.
let worker;
let next = 0;
const pending = new Map();

function start() {
    worker = new Worker(new URL('./simulation-worker.js', import.meta.url), { type: 'module' });
    worker.onmessage = ({ data }) => {
        const request = pending.get(data.id);
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

function call(method, args, transfer = []) {
    if (!worker) {
        start();
    }
    const id = next++;
    return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        worker.postMessage({ id, method, args }, transfer);
    });
}

export function render(netlist, input, sampleRate, oversample) {
    // NOTE: The copy moves to the worker without a second copy, and the caller keeps its input.
    const samples = Float64Array.from(input);
    return call('Render', [netlist, samples, sampleRate, oversample], [samples.buffer]);
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
