// N'oubliez pas de lier le fichier CSS dans votre HTML :
// <link rel="stylesheet" href="style.css">

(function () {
  "use strict";

  // =================================================================================
  // CONFIGURATION
  // =================================================================================
  const config = window.hoverTrailerConfig || {
    HOVER_DELAY: 700,
    DEBUG_LOGGING: true,
    PREVIEW_WIDTH: 400, // Largeur fixe en pixels pour une taille harmonisée
    ENABLE_PREVIEW_AUDIO: false,
    ENABLE_BACKGROUND_BLUR: true,
  };

  // =================================================================================
  // VARIABLES D'ÉTAT ET CACHE
  // =================================================================================
  let hoverTimeout;
  let hideTimeout;
  let currentPreview = null;
  let currentCardElement = null;
  let isClickTransitioning = false;
  const trailerCache = new Map();

  // =================================================================================
  // LOGGING
  // =================================================================================
  function log(message, ...args) {
    if (config.DEBUG_LOGGING) {
      console.log("[HoverTrailer]", message, ...args);
    }
  }

  // =================================================================================
  // FONCTIONS PRINCIPALES
  // =================================================================================

  function hidePreview() {
    clearTimeout(hoverTimeout);
    clearTimeout(hideTimeout);

    if (currentPreview) {
      const previewToRemove = currentPreview;
      previewToRemove.classList.remove("visible");
      previewToRemove.addEventListener(
        "transitionend",
        () => {
          if (previewToRemove.parentNode) {
            previewToRemove.remove();
          }
        },
        { once: true }
      );
    }

    removeBackgroundBlur();
    currentPreview = null;
    currentCardElement = null;
  }

  function showPreview(cardElement, movieId) {
    if (currentCardElement === cardElement) return;

    hidePreview();
    currentCardElement = cardElement;

    // Cible l'image pour un centrage précis. Fallback sur la carte entière.
    const imageElement =
      cardElement.querySelector(".cardImageContainer") || cardElement;
    const targetRect = imageElement.getBoundingClientRect();

    const loaderContainer = document.createElement("div");
    loaderContainer.className = "hover-trailer-container";

    // --- Calcul des dimensions et de la position ---
    const previewWidth = config.PREVIEW_WIDTH;
    const previewHeight = previewWidth * (9 / 16); // Force le ratio 16:9

    loaderContainer.style.top = `${targetRect.top + targetRect.height / 2}px`;
    loaderContainer.style.left = `${targetRect.left + targetRect.width / 2}px`;
    loaderContainer.style.width = `${previewWidth}px`;
    loaderContainer.style.height = `${previewHeight}px`;

    const loader = document.createElement("div");
    loader.className = "hover-trailer-loader";
    loaderContainer.appendChild(loader);
    document.body.appendChild(loaderContainer);
    currentPreview = loaderContainer;

    // Attache les écouteurs pour que la preview fasse partie de la "zone de survol"
    currentPreview.addEventListener("mouseenter", () =>
      clearTimeout(hideTimeout)
    );
    currentPreview.addEventListener(
      "mouseleave",
      () => (hideTimeout = setTimeout(hidePreview, 300))
    );

    requestAnimationFrame(() => currentPreview.classList.add("visible"));

    if (trailerCache.has(movieId)) {
      loadVideo(trailerCache.get(movieId), cardElement);
    } else {
      fetch(`/HoverTrailer/TrailerInfo/${movieId}`)
        .then((response) => {
          if (!response.ok) throw new Error("Trailer non trouvé");
          return response.json();
        })
        .then((trailerInfo) => {
          const trailerPath = trailerInfo.IsRemote
            ? trailerInfo.Path
            : `/Videos/${trailerInfo.Id}/stream`;
          trailerCache.set(movieId, trailerPath);
          loadVideo(trailerPath, cardElement);
        })
        .catch((error) => {
          log("Erreur de chargement:", error);
          if (currentPreview) currentPreview.innerHTML = "❌";
        });
    }
  }

  function loadVideo(trailerPath, cardElement) {
    if (cardElement !== currentCardElement) return;

    const video = document.createElement("video");
    video.addEventListener(
      "loadeddata",
      () => {
        if (cardElement !== currentCardElement || !currentPreview) return;

        currentPreview.innerHTML = "";
        currentPreview.appendChild(video);
        createMuteButton(currentPreview, video);
        applyBackgroundBlur();

        video.play().catch((e) => {
          log("Erreur de lecture:", e.name);
          if (e.name === "NotAllowedError" && !video.muted) {
            video.muted = true;
            video.play();
          }
        });
      },
      { once: true }
    );

    video.src = trailerPath;
    video.muted = !config.ENABLE_PREVIEW_AUDIO;
    video.loop = true;
    video.preload = "metadata";
  }

  function createMuteButton(container, video) {
    const button = document.createElement("div");
    button.className = "trailer-mute-button";
    const setIcon = () => {
      button.textContent = video.muted ? "🔇" : "🔊";
    };
    setIcon();
    button.addEventListener("click", (e) => {
      e.stopPropagation();
      video.muted = !video.muted;
      setIcon();
    });
    container.appendChild(button);
  }

  function applyBackgroundBlur() {
    /* ... (inchangé) ... */
  }
  function removeBackgroundBlur() {
    /* ... (inchangé) ... */
  }
  /* Collez ici les fonctions applyBackgroundBlur et removeBackgroundBlur du script précédent */

  // =================================================================================
  // ATTACHEMENT DES ÉVÉNEMENTS
  // =================================================================================
  function attachHoverListeners() {
    const movieCards = document.querySelectorAll(
      '.card[data-type="Movie"]:not([data-hover-attached]), .card[data-itemtype="Movie"]:not([data-hover-attached])'
    );

    movieCards.forEach((card) => {
      card.dataset.hoverAttached = "true";
      const movieId =
        card.getAttribute("data-id") || card.getAttribute("data-itemid");
      if (!movieId) return;

      card.addEventListener("mouseenter", () => {
        if (isClickTransitioning) return;
        clearTimeout(hideTimeout);
        clearTimeout(hoverTimeout);
        hoverTimeout = setTimeout(
          () => showPreview(card, movieId),
          config.HOVER_DELAY
        );
      });

      card.addEventListener("mouseleave", () => {
        clearTimeout(hoverTimeout);
        hideTimeout = setTimeout(hidePreview, 300);
      });

      card.addEventListener("click", () => {
        isClickTransitioning = true;
        hidePreview();
        setTimeout(() => {
          isClickTransitioning = false;
        }, 1500);
      });
    });
  }

  // =================================================================================
  // INITIALISATION
  // =================================================================================
  log("Script HoverTrailer (v2.4) initialisé.");
  const observer = new MutationObserver(attachHoverListeners);
  observer.observe(document.body, { childList: true, subtree: true });
  attachHoverListeners();
})();
