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

function enablePageImageViewer(content) {
    const viewer = document.getElementById("page-image-viewer");
    if (!content || typeof viewer?.showModal !== "function")
        return;

    const stage = viewer.querySelector(".page-image-stage");
    const closeButton = viewer.querySelector(".page-image-close");
    const info = viewer.querySelector(".page-image-info span");
    const download = viewer.querySelector(".page-image-info a");
    const largeLimit = Number(viewer.dataset.largeLimit);
    const interactive = "a, button, label, summary, [role=button], [role=link], [onclick], .youtube-placeholder";
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    closeButton.addEventListener("click", () => viewer.close());
    stage.addEventListener("click", () => viewer.close());
    viewer.addEventListener("close", () => {
        if (!viewer.open)
            stage.replaceChildren();
    });

    for (const image of content.querySelectorAll("img:not([usemap])")) {
        if (image.closest(interactive) || image.getAttribute("src")?.endsWith(viewer.dataset.disabledFragment))
            continue;

        image.classList.add("page-image-trigger");
        image.tabIndex = 0;
        image.setAttribute("role", "button");
        image.setAttribute("aria-haspopup", "dialog");
        image.setAttribute("aria-label", image.alt ? `View ${image.alt} at full size` : "View full-size image");

        image.addEventListener("click", () => openImage(image));
        image.addEventListener("keydown", (event) => {
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                openImage(image);
            }
        });
    }

    async function openImage(image) {
        const source = image.currentSrc || image.src;
        if (!source || viewer.open)
            return;

        const originalUrl = image.dataset.originalSrc ? new URL(image.dataset.originalSrc, document.baseURI) : null;
        const largeUrl = image.dataset.largeSrc;
        const name = originalUrl?.searchParams.get("name") || image.dataset.imageName || image.alt ||
            getPageImageFileName(source) || "Image";
        const { originalWidth, originalHeight } = image.dataset;
        const hasOriginalDimensions = Boolean(originalWidth && originalHeight);
        const preview = new Image();
        preview.src = source;
        preview.alt = image.alt;
        stage.replaceChildren(preview);
        download.href = originalUrl?.href || source;
        download.hidden = Boolean(largeUrl && !originalUrl);
        info.textContent = hasOriginalDimensions
            ? `${name} · ${originalWidth} × ${originalHeight} px original`
            : name;

        // The modal returns focus here when it closes, including after a mouse or touch click.
        image.focus({preventScroll: true});
        const sourceBounds = image.getBoundingClientRect();
        viewer.showModal();

        let opening;
        if (!reducedMotion.matches)
            opening = animatePageImageOpening(preview, sourceBounds, image.naturalWidth, image.naturalHeight);

        const current = () => viewer.open && preview.isConnected;
        let displayed = preview;
        if (largeUrl && largeUrl !== source) {
            const large = new Image();
            large.src = largeUrl;
            large.alt = image.alt;

            try {
                await large.decode();
                await opening?.finished;
                if (!current())
                    return;

                displayed = large;
                preview.setAttribute("aria-hidden", "true");
                stage.appendChild(large);
                if (!reducedMotion.matches)
                    await large.animate([{opacity: 0}, {opacity: 1}], {duration: 160}).finished;

                preview.remove();
            } catch {
                // Leave the page-size image in place if the large version can't be loaded.
            }
        }

        if (hasOriginalDimensions)
            return;

        try {
            await displayed.decode();
        } catch {
            return;
        }

        if (!viewer.open || !displayed.isConnected)
            return;

        const displayedSize = Math.max(displayed.naturalWidth, displayed.naturalHeight);
        // The large variant keeps the original dimensions when neither edge reaches the resize limit.
        const sizeLabel = !largeUrl || (displayed !== preview && displayedSize < largeLimit)
            ? "original"
            : "displayed";
        info.textContent = `${name} · ${displayed.naturalWidth} × ${displayed.naturalHeight} px ${sizeLabel}`;
    }
}

function animatePageImageOpening(image, source, width, height) {
    if (!width || !height || !source.width || !source.height)
        return;

    const target = image.getBoundingClientRect();
    const fit = Math.min(target.width / width, target.height / height);
    if (!fit)
        return;

    const scale = Math.min(source.width / (width * fit), source.height / (height * fit));
    const x = source.left + source.width / 2 - (target.left + target.width / 2);
    const y = source.top + source.height / 2 - (target.top + target.height / 2);
    return image.animate([
        {transform: `translate(${x}px, ${y}px) scale(${scale})`},
        {transform: "none"},
    ], {duration: 240, easing: "ease-out"});
}

function getPageImageFileName(source) {
    try {
        const url = new URL(source, document.baseURI);
        return decodeURIComponent(url.pathname.split("/").pop());
    } catch {
        return "";
    }
}
