// Interop methods for ThriveDevCenter
"use strict";

function addToHistory(uri){
    // Skip if not supported
    if(!history)
        return;

    // TODO: remove
    console.log("history push:", uri);

    history.pushState(null, "", uri);
}
