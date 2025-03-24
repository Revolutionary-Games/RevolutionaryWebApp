// Support JS for RevolutionaryWebApp rendered HTML pages

// YouTube support
function detectYouTubeVideos(parentElement) {
    let autoLoad = false;

    try {
        if (getYouTubeAlwaysEnabled())
            autoLoad = true;
    } catch (e) {
        console.log(e);
    }

    for (const placeholder of parentElement.querySelectorAll(".youtube-placeholder")) {
        const videoId = placeholder.getAttribute("data-video-id");
        const embedContainer = placeholder.querySelector(".youtube-embed-container");
        const thumbnailContainer = placeholder.querySelector(".thumbnail-container");

        function loadYouTubePlayer(autoPlay, withCookieConsent = false) {
            const autoPlayAllow = autoPlay ? "autoplay;" : "";
            autoPlay = autoPlay ? "1" : "0";
            const cookieParam = withCookieConsent ? "&cookie-consent=1" : "";
            const iframe = document.createElement("iframe");
            iframe.setAttribute("src",
                `https://www.youtube.com/embed/${videoId}?autoplay=${autoPlay}${cookieParam}`);
            iframe.setAttribute("frameborder", "0");
            iframe.setAttribute("allow",
                `accelerometer; ${autoPlayAllow} clipboard-write; encrypted-media; gyroscope; picture-in-picture; fullscreen; screen-wake-lock`);
            iframe.setAttribute("allowfullscreen", "true");

            embedContainer.innerHTML = "";
            embedContainer.appendChild(iframe);
            embedContainer.style.display = "block";
            thumbnailContainer.style.display = "none";
            placeholder.querySelector(".youtube-controls").style.display = "none";
        }

        if (autoLoad) {
            loadYouTubePlayer(false);
            continue;
        }

        thumbnailContainer.addEventListener("click", function () {
            loadYouTubePlayer(true);
        });

        placeholder.querySelector(".accept-cookies").addEventListener("click", function () {
            loadYouTubePlayer(true);
            saveYouTubePreference(true);
        });
    }
}

function getYouTubeAlwaysEnabled() {
    const preference = localStorage.getItem("ThriveYTCookies");
    return preference === "true";
}

function saveYouTubePreference(enabled) {
    localStorage.setItem("ThriveYTCookies", enabled.toString());
}

