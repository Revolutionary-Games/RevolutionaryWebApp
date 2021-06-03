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

function scrollToElement(id) {
    const element = document.getElementById(id);

    if (element instanceof HTMLElement) {
        element.scrollIntoView({
            behavior: "smooth",
            block: "end",
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

// Hack needed for putFormFile to work
function storeFileInputFilesForLaterUse(inputElementId) {
    const inputElement = document.getElementById(inputElementId);

    for (const file of inputElement.files)
        fileObjects[file.name] = file;
}

// C# can't properly stream file bodies, so this helper is used to put form files
function putFormFile(fileName, url) {
    const file = fileObjects[fileName];

    if (!file)
        return "Form file object not found";

    delete fileObjects[fileName];

    return new Promise((resolve, reject) => {
        fetch(url, {
            method: 'PUT',
            headers: {
                "Content-Type": file.type ?? "application/octet-stream"
            },
            body: file
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
    })
}
