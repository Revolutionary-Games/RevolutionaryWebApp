// Interop methods for ThriveDevCenter
"use strict";

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

function registerFileDropArea(element) {
    // Looks like there's no way to reliably check this from C# so this check is here
    if(!element)
        return;

    element.addEventListener("drop", dropAreaReceivedDrop, false);
}

function dropAreaReceivedDrop(event) {
    event.preventDefault();
    console.log("Got event:", event);
}
