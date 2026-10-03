// The simulation worker. It starts a second .NET runtime from the files of the web app and calls the
// [JSExport] methods of Ssp.Web.Hosting.WorkerExports. The first message gives the URL of dotnet.js.
let exports;

async function boot(url) {
    // NOTE: Without this flag, dotnet.js takes a worker that has onmessage for a runtime thread and waits for the
    // main runtime. The start then never ends.
    globalThis.dotnetSidecar = true;
    const { dotnet } = await import(url);
    const runtime = await dotnet.create();
    const assembly = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return assembly.Ssp.Web.Hosting.WorkerExports;
}

self.onmessage = async ({ data }) => {
    if (data.dotnet !== undefined) {
        exports = boot(data.dotnet);
        return;
    }
    try {
        const methods = await exports;
        self.postMessage({ id: data.id, json: methods[data.method](...data.args) });
    } catch (error) {
        self.postMessage({ id: data.id, error: String(error) });
    }
};
