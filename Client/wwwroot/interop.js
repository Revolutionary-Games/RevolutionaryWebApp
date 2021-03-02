// Interop methods for ThriveDevCenter
"use strict";

function addToHistory(uri){
    // Skip if not supported
    if(!history) {
        console.log("History is not supported on this browser");
        return;
    }

    // TODO: remove
    console.log("history push:", uri);

    history.pushState(null, "", uri);
}

function getCurrentURL(){
    return window.location.href;
}
