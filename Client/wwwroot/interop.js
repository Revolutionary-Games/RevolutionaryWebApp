// Interop methods for ThriveDevCenter
"use strict";

// Hack to pass around proper File objects with the logic in C#
const fileObjects = {};

function addToHistory(uri) {
    // Skip if not supported
    if (!history) {
        console.log("History is not supported on this browser");
        return;
    }

    history.pushState(null, "", uri);
}

function getCurrentURL() {
    return window.location.href;
}

function getCSRFToken() {
    return document.getElementById("csrfUserToken").value;
}

function getCSRFTokenExpiry() {
    return document.getElementById("csrfTokenExpiryTimestamp").value;
}

function getStaticHomePageNotice() {
    const element = document.getElementById("homePageNoticeTextSource");

    if (!element)
        return null;

    return element.value;
}

function scrollToElement(id, smooth) {
    const element = document.getElementById(id);

    if (element instanceof HTMLElement) {
        element.scrollIntoView({
            behavior: smooth ? "smooth" : "auto",
            block: "center",
            inline: "nearest"
        });
    }
}

// Helper for getting dropped files into C# by going through an InputFile
// This workaround is needed until this feature is done: https://github.com/dotnet/aspnetcore/issues/18754
function registerFileDropArea(dropElement, inputElementId) {
    // Looks like there's no way to reliably check this from C# so this check is here
    if (!dropElement)
        return;

    // Skip duplicate listeners
    if (dropElement.dataset.dropListen === inputElementId)
        return;

    dropElement.addEventListener("drop", (event) => {
        event.preventDefault();

        const inputElement = document.getElementById(inputElementId);
        inputElement.files = event.dataTransfer.files;

        // Need to trigger an event to make the C# code notice this
        const changeEvent = new Event('change');
        inputElement.dispatchEvent(changeEvent);

    }, false);

    dropElement.dataset.dropListen = inputElementId;
}

// Hack needed for putFormFile (and chunked variant) to work
function storeFileInputFilesForLaterUse(inputElementId) {
    const inputElement = document.getElementById(inputElementId);

    for (const file of inputElement.files)
        fileObjects[file.name] = file;
}

// C# can't properly stream file bodies, so this helper is used to put form files
function putFormFile(fileName, url) {
    const file = fileObjects[fileName];

    if (!file) {
        return new Promise((resolve, reject) => {
            reject("Form file object not found");
        });
    }

    delete fileObjects[fileName];

    return performPut(url, file.type, file);
}

// This variant allows sending a part of the file to an url (used for multipart uploads).
// Call reportFormFileChunksUploaded after uploading all parts.
function putFormFileChunk(fileName, url, offset, length) {
    const file = fileObjects[fileName];

    if (!file) {
        return new Promise((resolve, reject) => {
            reject("Form file object not found");
        });
    }

    console.log("Starting chunk upload " + fileName + " offset: " + offset + " length: " + length);
    const chunkEnd = offset + length;

    /* "Content-Range": "bytes " + offset + "-" + (chunkEnd - 1) + "/" + file.size, */
    return performPut(url, file.type, file.slice(offset, chunkEnd));
}

function reportFormFileChunksUploaded(fileName) {
    delete fileObjects[fileName];
}

function performPut(url, type, body) {
    return new Promise((resolve, reject) => {
        fetch(url, {
            method: 'PUT',
            headers: {
                "Content-Type": type ?? "application/octet-stream"
            },
            body: body
        }).then(response => {
            if (!response.ok) {
                response.text().then(text => {
                    resolve("Invalid response from server (" + response.status + "): " + text);
                }).catch(error => {
                    resolve("Invalid response from server (" + response.status + "): (failed to read body)");
                })
            } else {
                resolve();
            }
        }).catch(error => {
            let extra = "";
            if (error.response) {
                extra = " server responded with (" + error.response.status + ")";
            }

            resolve("Fetch error: " + error + extra);
        });
    });
}

// TODO: when moving to .NET 6 update this to use a more efficient approach
// The following 3 functions are mostly copied as-is from:
// https://www.meziantou.net/generating-and-downloading-a-file-in-a-blazor-webassembly-application.htm
function downloadFileFromBytes(filename, contentType, content) {
    // Blazor marshall byte[] to a base64 string, so we first need to convert
    // the string (content) to a Uint8Array to create the File
    const data = base64DecToArr(content);

    // Create the URL
    const file = new File([data], filename, {type: contentType});
    const exportUrl = URL.createObjectURL(file);

    // Create the <a> element and click on it
    const a = document.createElement("a");
    document.body.appendChild(a);
    a.href = exportUrl;
    a.download = filename;
    a.target = "_self";
    a.click();

    // We don't need to keep the object url, let's release the memory
    // On Safari it seems you need to comment this line... (please let me know if you know why)
    URL.revokeObjectURL(exportUrl);
}

// Convert a base64 string to a Uint8Array. This is needed to create a blob object from the base64 string.
// The code comes from: https://developer.mozilla.org/fr/docs/Web/API/WindowBase64/D%C3%A9coder_encoder_en_base64
function b64ToUint6(nChr) {
    return nChr > 64 && nChr < 91 ? nChr - 65 : nChr > 96 && nChr < 123 ? nChr - 71 : nChr > 47 && nChr < 58 ? nChr + 4 : nChr === 43 ? 62 : nChr === 47 ? 63 : 0;
}

function base64DecToArr(sBase64, nBlocksSize) {
    const sB64Enc = sBase64.replace(/[^A-Za-z0-9+\/]/g, ""),
        nInLen = sB64Enc.length,
        nOutLen = nBlocksSize ? Math.ceil((nInLen * 3 + 1 >> 2) / nBlocksSize) * nBlocksSize : nInLen * 3 + 1 >> 2,
        taBytes = new Uint8Array(nOutLen);

    let nMod3, nMod4, nUint24 = 0, nOutIdx = 0, nInIdx = 0;
    for (; nInIdx < nInLen; nInIdx++) {
        nMod4 = nInIdx & 3;
        nUint24 |= b64ToUint6(sB64Enc.charCodeAt(nInIdx)) << 18 - 6 * nMod4;
        if (nMod4 === 3 || nInLen - nInIdx === 1) {
            for (nMod3 = 0; nMod3 < 3 && nOutIdx < nOutLen; nMod3++, nOutIdx++) {
                taBytes[nOutIdx] = nUint24 >>> (16 >>> nMod3 & 24) & 255;
            }
            nUint24 = 0;
        }
    }
    return taBytes;
}
