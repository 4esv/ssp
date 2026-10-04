// Plays rendered audio with WebAudio and saves a WAV file. Used by Components/ClipPlayer.razor.
let context;
let source;

export function play(bytes, sampleRate) {
    // NOTE: The bytes are 32-bit float samples. The copy makes the buffer start at a multiple of 4.
    const samples = new Float32Array(bytes.slice().buffer);
    const buffer = new AudioBuffer({ length: samples.length, sampleRate, numberOfChannels: 1 });
    buffer.copyToChannel(samples, 0);
    globalThis.sspClipBuffer = buffer;

    context ??= new AudioContext();
    context.resume();
    source?.stop();
    source = context.createBufferSource();
    source.buffer = buffer;
    source.connect(context.destination);
    source.start();
}

export function download(name, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type: 'audio/wav' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
}
