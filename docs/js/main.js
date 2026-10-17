/**
 * WireFox Official Landing Page Scripts
 * Handles mobile browser detection & polite notice, copy-to-clipboard (with legacy fallback),
 * screenshot showcase tabs, installation tabs, FAQ accordion, and GitHub release metadata.
 */

document.addEventListener('DOMContentLoaded', () => {
  initMobileNotice();
  initClipboardButtons();
  initShowcaseTabs();
  initInstallTabs();
  initFaqAccordion();
  fetchLatestReleaseInfo();
});

/* -------------------------------------------------------------------------- */
/* Mobile Browser Detection & Polite Notice                                   */
/* -------------------------------------------------------------------------- */
function initMobileNotice() {
  const notice = document.getElementById('mobile-notice');
  const closeBtn = document.getElementById('mobile-notice-close');
  if (!notice) return;

  const updateNoticeVisibility = () => {
    // Respect user dismissal within current session
    if (sessionStorage.getItem('wirefox_mobile_dismissed') === '1') {
      notice.style.display = 'none';
      return;
    }

    // Detect mobile user agents, touch screens, or narrow responsive viewports
    const isMobileUA = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini|Mobile/i.test(navigator.userAgent);
    const isSmallTouchScreen = window.innerWidth <= 840 && ('ontouchstart' in window || navigator.maxTouchPoints > 0);
    const isNarrowViewport = window.innerWidth <= 680;

    if (isMobileUA || isSmallTouchScreen || isNarrowViewport) {
      notice.style.display = 'block';
    } else {
      notice.style.display = 'none';
    }
  };

  updateNoticeVisibility();
  window.addEventListener('resize', updateNoticeVisibility);

  // Dismissal handler
  if (closeBtn) {
    closeBtn.addEventListener('click', () => {
      notice.style.display = 'none';
      try {
        sessionStorage.setItem('wirefox_mobile_dismissed', '1');
      } catch (e) {
        // Storage might be restricted in private browsing mode
      }
    });
  }
}

/* -------------------------------------------------------------------------- */
/* Universal Copy to Clipboard (with fallback for mobile & insecure contexts)  */
/* -------------------------------------------------------------------------- */
async function copyTextToClipboard(text) {
  if (!text) return false;

  // Modern Async Clipboard API (available in secure contexts)
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch (err) {
      console.warn('navigator.clipboard failed, attempting fallback textarea: ', err);
    }
  }

  // Fallback for non-secure contexts, older browsers, or mobile webviews
  try {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.top = '-9999px';
    textarea.style.left = '-9999px';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    const successful = document.execCommand('copy');
    document.body.removeChild(textarea);
    return successful;
  } catch (err) {
    console.error('Fallback execCommand copy failed: ', err);
    return false;
  }
}

function initClipboardButtons() {
  const copyButtons = document.querySelectorAll('.btn-copy');

  copyButtons.forEach(btn => {
    btn.addEventListener('click', async () => {
      const targetId = btn.getAttribute('data-target');
      const directText = btn.getAttribute('data-copy');
      let textToCopy = '';

      if (targetId) {
        const targetEl = document.getElementById(targetId);
        if (targetEl) {
          textToCopy = (targetEl.innerText || targetEl.textContent || '').trim();
        }
      } else if (directText) {
        textToCopy = directText.trim();
      }

      if (!textToCopy) return;

      const success = await copyTextToClipboard(textToCopy);
      if (!success) return;

      const originalHtml = btn.innerHTML;
      btn.classList.add('copied');
      btn.innerHTML = `
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="20 6 9 17 4 12"></polyline>
        </svg>
        <span>Copied!</span>
      `;

      setTimeout(() => {
        btn.classList.remove('copied');
        btn.innerHTML = originalHtml;
      }, 2000);
    });
  });
}

/* -------------------------------------------------------------------------- */
/* Screenshot Showcase Tabs                                                   */
/* -------------------------------------------------------------------------- */
function initShowcaseTabs() {
  const tabs = document.querySelectorAll('.showcase-tab');
  const slides = document.querySelectorAll('.showcase-slide');
  const captionTitle = document.getElementById('caption-title');
  const captionDesc = document.getElementById('caption-desc');

  if (!tabs.length || !slides.length) return;

  tabs.forEach(tab => {
    const activateTab = () => {
      const targetSlideId = tab.getAttribute('data-slide');

      // Update tab active states
      tabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');

      // Update slide active states
      slides.forEach(slide => {
        if (slide.id === targetSlideId) {
          slide.classList.add('active');
          if (captionTitle && tab.getAttribute('data-title')) {
            captionTitle.textContent = tab.getAttribute('data-title');
          }
          if (captionDesc && tab.getAttribute('data-desc')) {
            captionDesc.textContent = tab.getAttribute('data-desc');
          }
        } else {
          slide.classList.remove('active');
        }
      });
    };

    tab.addEventListener('click', activateTab);
    tab.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        activateTab();
      }
    });
  });
}

/* -------------------------------------------------------------------------- */
/* Installation Tabs                                                          */
/* -------------------------------------------------------------------------- */
function initInstallTabs() {
  const tabBtns = document.querySelectorAll('.install-tab-btn');
  const panes = document.querySelectorAll('.install-pane');

  if (!tabBtns.length || !panes.length) return;

  tabBtns.forEach(btn => {
    const switchPane = () => {
      const targetPaneId = btn.getAttribute('data-pane');

      tabBtns.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      panes.forEach(pane => {
        if (pane.id === targetPaneId) {
          pane.classList.add('active');
        } else {
          pane.classList.remove('active');
        }
      });
    };

    btn.addEventListener('click', switchPane);
    btn.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        switchPane();
      }
    });
  });
}

/* -------------------------------------------------------------------------- */
/* FAQ Accordion                                                              */
/* -------------------------------------------------------------------------- */
function initFaqAccordion() {
  const faqItems = document.querySelectorAll('.faq-item');

  faqItems.forEach(item => {
    const questionBtn = item.querySelector('.faq-question');
    if (!questionBtn) return;

    const toggleAccordion = () => {
      const isOpen = item.classList.contains('open');

      // Close all other items for clean accordion UX
      faqItems.forEach(i => i.classList.remove('open'));

      if (!isOpen) {
        item.classList.add('open');
      }
    };

    questionBtn.addEventListener('click', toggleAccordion);
    questionBtn.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        toggleAccordion();
      }
    });
  });
}

/* -------------------------------------------------------------------------- */
/* GitHub Release Fetcher                                                     */
/* -------------------------------------------------------------------------- */
async function fetchLatestReleaseInfo() {
  const versionBadges = document.querySelectorAll('.release-version-tag');
  const downloadLinks = document.querySelectorAll('#hero-download-btn, #nav-download-btn, #cta-download-btn, #mobile-download-btn, a[href*="releases/latest"]');

  try {
    const res = await fetch('https://api.github.com/repos/TalviFox/WireFox/releases/latest');
    if (!res.ok) return;

    const data = await res.json();
    if (data && data.tag_name) {
      const version = data.tag_name; // e.g. "v1.0.5"

      versionBadges.forEach(badge => {
        badge.textContent = version;
      });

      // Point download links to the Windows .exe asset if uploaded to release
      if (data.assets && data.assets.length > 0) {
        const exeAsset = data.assets.find(a => a.name && a.name.toLowerCase().endsWith('.exe'));
        if (exeAsset && exeAsset.browser_download_url) {
          downloadLinks.forEach(link => {
            link.href = exeAsset.browser_download_url;
          });
        }
      }
    }
  } catch (e) {
    // Graceful offline fallback: preserves hardcoded default release tags
    console.debug('Using cached release version info.');
  }
}
