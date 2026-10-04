// Saves a data URL as a file. A click on a temporary link starts the download.
export function save(name, href) {
    const a = document.createElement("a");
    a.download = name;
    a.href = href;
    a.click();
}
