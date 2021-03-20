// Interop methods for ThriveDevCenter
"use strict";

function addToHistory(uri){
    // Skip if not supported
    if(!history) {
        console.log("History is not supported on this browser");
        return;
    }

    history.pushState(null, "", uri);
}

function getCurrentURL(){
    return window.location.href;
}

function getCSRFToken(){
    return document.getElementById("csrfUserToken").value;
}

function getCSRFTokenExpiry(){
    return document.getElementById("csrfTokenExpiryTimestamp").value;
}
