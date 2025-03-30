"use strict";

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

function enableAllCookieForms(){
    for (const form of document.querySelectorAll(".youtube-cookie-form")) {
        let enabled = false;
        try {
            enabled = getYouTubeAlwaysEnabled();
        } catch{
            form.appendChild(document.createTextNode("Local storage is not available, setting will not be saved!"));
            continue;
        }

        const radioButtons = form.elements['youtube-cookie-preference'];

        function getCurrentSelection() {
            for (const radioButton of radioButtons) {
                if (radioButton.checked) {
                    return radioButton.value;
                }
            }
            return null;
        }

        function setSelection(value) {
            for (const radioButton of radioButtons) {
                if (radioButton.value === value) {
                    radioButton.checked = true;
                    break;
                }
            }
        }

        // Initialise the form with the stored preference
        setSelection(enabled.toString());

        for (const radioButton of radioButtons) {
            radioButton.addEventListener("change", function() {
                const newValue = getCurrentSelection();
                console.log("Setting YouTube cookie preference to:", newValue);
                saveYouTubePreference(newValue);
            });
        }
    }
}

function getYouTubeAlwaysEnabled() {
    const preference = localStorage.getItem("ThriveYTCookies");
    return preference === "true";
}

function saveYouTubePreference(enabled) {
    localStorage.setItem("ThriveYTCookies", enabled.toString());
}

// Mobile navigation implementation
function enableMobileNavigation(){
    const hamburgerButton = document.getElementById('nav-menu-button');
    const mobileNav = document.getElementById('mobile-nav');
    const backdrop = document.getElementById('backdrop');

    // Toggle navigation when hamburger is clicked
    hamburgerButton.addEventListener('click', function() {
        hamburgerButton.classList.toggle('active');
        mobileNav.classList.toggle('active');
        backdrop.classList.toggle('active');

        const isExpanded = hamburgerButton.classList.contains('active');
        hamburgerButton.setAttribute('aria-expanded', isExpanded.toString());
    });

    // Close the menu when clicking on a nav link
    const navLinks = document.querySelectorAll('.mobile-nav a');
    navLinks.forEach(link => {
        link.addEventListener('click', function() {
            hamburgerButton.classList.remove('active');
            mobileNav.classList.remove('active');
            backdrop.classList.remove('active');
            hamburgerButton.setAttribute('aria-expanded', 'false');
        });
    });

    // Close the menu when clicking outside
    document.addEventListener('click', function(event) {
        if (!event.target.closest('.mobile-header') &&
            !event.target.closest('.mobile-nav') &&
            mobileNav.classList.contains('active'))
        {
            hamburgerButton.classList.remove('active');
            mobileNav.classList.remove('active');
            backdrop.classList.remove('active');
            hamburgerButton.setAttribute('aria-expanded', 'false');
        }
    });
}
