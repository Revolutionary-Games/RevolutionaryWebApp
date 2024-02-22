// Interop methods for RevolutionaryWebApp
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

// Render latex math in element children
function renderMath(element) {
    const targets = element.getElementsByClassName("math");
    for (const el of targets) {
        let block = false;
        let text = el.textContent;

        // Need to do a bit of processing here to convert to katex working format from Markdig
        if (text.startsWith("\\(")) {
            text = text.substring(2, text.length - 2);
        } else {
            let start = text.indexOf("\\[");
            if (start !== -1) {
                start += 2;
                let end = text.lastIndexOf("\\]");
                if (end === -1)
                    end = text.length - 2;
                text = text.substring(start, end);
                block = true;
            }
        }

        katex.render(text, el, {
            throwOnError: false,
            displayMode: block,
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
    return new Promise((resolve, _) => {
        window.fetch(url, {
            method: 'PUT',
            headers: {
                "Content-Type": type ?? "application/octet-stream"
            },
            body: body
        }).then(response => {
            if (!response.ok) {
                response.text().then(text => {
                    resolve("Invalid response from server (" + response.status + "): " + text);
                }).catch(_ => {
                    resolve("Invalid response from server (" + response.status + "): (failed to read body)");
                });
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

// The following function is copied from:
// https://www.meziantou.net/generating-and-downloading-a-file-in-a-blazor-webassembly-application.htm
function downloadFileFromBytes(filename, contentType, content) {
    // Create the URL
    const file = new File([content], filename, {type: contentType});
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
