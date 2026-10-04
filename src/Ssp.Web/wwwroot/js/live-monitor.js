// Captures the microphone in chunks, gives each chunk to the monitor session, and plays the output chunks.
// Used by Components/LiveMonitor.razor. The latency is the time from the first captured sample of a chunk
// to the start of its playback, on the clock of one AudioContext.
const MaxQueue = 8;

const processor = `
class Capture extends AudioWorkletProcessor {
    constructor(options) {
        super();
        this.size = options.processorOptions.size;
        this.buffer = new Float32Array(this.size);
        this.filled = 0;
        this.start = 0;
    }
    process(inputs) {
        const channel = inputs[0][0];
        if (channel) {
            for (let i = 0; i < channel.length; i++) {
                if (this.filled === 0) {
                    this.start = currentTime + i / sampleRate;
                }
                this.buffer[this.filled++] = channel[i];
                if (this.filled === this.size) {
                    this.port.postMessage({ samples: this.buffer, time: this.start });
                    this.buffer = new Float32Array(this.size);
                    this.filled = 0;
                }
            }
        }
        return true;
    }
}
registerProcessor('ssp-capture', Capture);
`;

let context;
let stream;
let node;
let running = false;
let queue = [];
let nextTime = 0;
let dropped = 0;

export async function open() {
    stream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false },
    });
    context = new AudioContext({ latencyHint: 'interactive' });
    await context.resume();
    return context.sampleRate;
}

export async function begin(reference, size) {
    const url = URL.createObjectURL(new Blob([processor], { type: 'text/javascript' }));
    await context.audioWorklet.addModule(url);
    URL.revokeObjectURL(url);
    node = new AudioWorkletNode(context, 'ssp-capture', { processorOptions: { size } });
    // NOTE: The node has no output, so it does not play the microphone. A muted gain keeps the graph pulling it.
    const mute = new GainNode(context, { gain: 0 });
    context.createMediaStreamSource(stream).connect(node);
    node.connect(mute).connect(context.destination);

    globalThis.sspMonitorOutput = { length: 0, nonzero: 0 };
    queue = [];
    dropped = 0;
    nextTime = 0;
    running = true;
    node.port.onmessage = ({ data }) => {
        queue.push(data);
        while (queue.length > MaxQueue) {
            queue.shift();
            dropped++;
        }
    };
    pump(reference);
}

async function pump(reference) {
    while (running) {
        const chunk = queue.shift();
        if (!chunk) {
            await new Promise(resolve => setTimeout(resolve, 5));
            continue;
        }
        let output;
        try {
            output = await reference.invokeMethodAsync('Process', Array.from(chunk.samples));
        } catch (error) {
            // NOTE: Process turns a failed chunk into silence, so this is an interop failure. Say so, then stop.
            if (running) {
                await reference.invokeMethodAsync('Fail', String(error?.message ?? error)).catch(() => {});
            }
            break;
        }
        if (!running) {
            break;
        }
        const latency = play(output, chunk.time);
        const stats = globalThis.sspMonitorOutput;
        stats.length += output.length;
        stats.nonzero += output.reduce((count, sample) => count + (Math.abs(sample) > 1e-9 ? 1 : 0), 0);
        await reference.invokeMethodAsync('Report', latency, dropped);
    }
}

function play(samples, capturedAt) {
    const buffer = new AudioBuffer({ length: samples.length, sampleRate: context.sampleRate, numberOfChannels: 1 });
    buffer.copyToChannel(Float32Array.from(samples), 0);
    const source = context.createBufferSource();
    source.buffer = buffer;
    source.connect(context.destination);
    const start = Math.max(nextTime, context.currentTime);
    source.start(start);
    nextTime = start + buffer.duration;
    return (start - capturedAt) * 1000;
}

export async function close() {
    running = false;
    node?.disconnect();
    stream?.getTracks().forEach(track => track.stop());
    await context?.close();
    node = stream = context = undefined;
}
