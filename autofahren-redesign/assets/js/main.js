/*
 * A–Z Verkehrsschule Ostschweiz AG – Redesign
 * Interaktionen: Header, Mobile-Menü, Scroll-Reveal, Scrollspy, E-Mail, Frist-Rechner
 * Autor: N.Stillhart
 * Datum: 2026-09-28
 */
(() => {
  'use strict';

  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ---------- Header: Hintergrund beim Scrollen ---------- */
  const header = document.querySelector('[data-header]');
  const mobileCta = document.querySelector('[data-mobile-cta]');

  const onScroll = () => {
    const y = window.scrollY;
    header.classList.toggle('is-scrolled', y > 8);
    if (mobileCta) mobileCta.classList.toggle('is-visible', y > 560);
  };
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });

  /* ---------- Mobile-Menü ---------- */
  const burger = document.querySelector('[data-burger]');
  const menu = document.querySelector('[data-menu]');

  const setMenu = (open) => {
    burger.setAttribute('aria-expanded', String(open));
    burger.setAttribute('aria-label', open ? 'Menü schliessen' : 'Menü öffnen');
    document.body.classList.toggle('menu-open', open);
    if (open) {
      menu.hidden = false;
      requestAnimationFrame(() => menu.classList.add('is-open'));
    } else {
      menu.classList.remove('is-open');
      window.setTimeout(() => { if (!menu.classList.contains('is-open')) menu.hidden = true; }, 250);
    }
  };

  burger.addEventListener('click', () => setMenu(burger.getAttribute('aria-expanded') !== 'true'));
  menu.addEventListener('click', (e) => { if (e.target.closest('a')) setMenu(false); });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && burger.getAttribute('aria-expanded') === 'true') {
      setMenu(false);
      burger.focus();
    }
  });
  window.matchMedia('(min-width: 961px)').addEventListener('change', (e) => { if (e.matches) setMenu(false); });

  /* ---------- Scroll-Reveal ---------- */
  const revealEls = document.querySelectorAll('.reveal');

  // leichte Staffelung für Elemente, die nebeneinander stehen
  revealEls.forEach((el) => {
    const siblings = [...el.parentElement.children].filter((c) => c.classList.contains('reveal'));
    const index = siblings.indexOf(el);
    if (index > 0) el.style.setProperty('--delay', `${Math.min(index, 5) * 70}ms`);
  });

  if (reducedMotion || !('IntersectionObserver' in window)) {
    revealEls.forEach((el) => el.classList.add('is-visible'));
  } else {
    const revealObserver = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          revealObserver.unobserve(entry.target);
        }
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    revealEls.forEach((el) => revealObserver.observe(el));
  }

  /* ---------- Scrollspy: aktiver Navigationspunkt ---------- */
  const spyLinks = [...document.querySelectorAll('[data-spy]')];
  const spyTargets = spyLinks
    .map((link) => document.querySelector(link.getAttribute('href')))
    .filter(Boolean);

  if ('IntersectionObserver' in window && spyTargets.length) {
    const spyObserver = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        spyLinks.forEach((link) => {
          link.classList.toggle('is-active', link.getAttribute('href') === `#${entry.target.id}`);
        });
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    spyTargets.forEach((section) => spyObserver.observe(section));
  }

  /* ---------- Hero-Animation bei reduzierter Bewegung anhalten ---------- */
  const roadSvg = document.querySelector('.road');
  if (reducedMotion && roadSvg && typeof roadSvg.pauseAnimations === 'function') {
    roadSvg.pauseAnimations();
    roadSvg.setCurrentTime(3.2);
  }

  /* ---------- E-Mail-Adresse zusammensetzen (Schutz vor Spam-Bots) ---------- */
  document.querySelectorAll('[data-mail]').forEach((el) => {
    const address = `${el.dataset.mail}@${el.dataset.domain}`;
    el.href = `mailto:${address}`;
    el.textContent = address;
  });

  /* ---------- Jahr im Footer ---------- */
  const year = document.querySelector('[data-year]');
  if (year) year.textContent = String(new Date().getFullYear());

  /* ---------- Frist-Rechner ---------- */
  const form = document.querySelector('[data-deadline]');
  if (form) {
    const input = form.querySelector('input[type="date"]');
    const output = form.querySelector('[data-deadline-output]');
    const bookingUrl = 'https://www.autofahren.ch/?s=registration&cat=2PH';
    const dateFormat = new Intl.DateTimeFormat('de-CH', { day: 'numeric', month: 'long', year: 'numeric' });
    const DAY = 24 * 60 * 60 * 1000;

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const toIso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    input.max = toIso(today);

    // Prüfungsdatum + 1 Jahr (29. Februar wird zum 28. Februar)
    const addOneYear = (d) => {
      const next = new Date(d.getFullYear() + 1, d.getMonth(), d.getDate());
      if (next.getMonth() !== d.getMonth()) next.setDate(0);
      return next;
    };

    const render = () => {
      output.classList.remove('is-late');
      if (!input.value) {
        output.innerHTML = '<span class="deadline__hint">Wähle ein Datum, um deine Frist zu sehen.</span>';
        return;
      }

      const [y, m, d] = input.value.split('-').map(Number);
      const exam = new Date(y, m - 1, d);
      if (Number.isNaN(exam.getTime()) || exam > today) {
        output.innerHTML = '<span class="deadline__hint">Bitte wähle ein Datum, das nicht in der Zukunft liegt.</span>';
        return;
      }

      const deadline = addOneYear(exam);
      const days = Math.round((deadline - today) / DAY);
      const formatted = dateFormat.format(deadline);

      if (days < 0) {
        output.classList.add('is-late');
        output.innerHTML = `
          <span class="deadline__date">${formatted}</span>
          <span class="deadline__meta">Diese Frist ist bereits abgelaufen. Bitte melde dich direkt bei unserem Sekretariat.</span>`;
        return;
      }

      const remaining = days === 0 ? '<strong>heute</strong> ist der letzte Tag' : `noch <strong>${days} Tag${days === 1 ? '' : 'e'}</strong>`;
      output.innerHTML = `
        <span class="deadline__date">bis ${formatted}</span>
        <span class="deadline__meta">Spätester Termin für deinen WAB-Kurstag – ${remaining}.</span>
        <a class="btn btn--primary" href="${bookingUrl}">Jetzt Termin buchen
          <svg class="btn__arrow" viewBox="0 0 16 16" aria-hidden="true"><path d="M3 8h10M9 4l4 4-4 4"/></svg>
        </a>`;
    };

    input.addEventListener('input', render);
    input.addEventListener('change', render);
    form.addEventListener('submit', (e) => { e.preventDefault(); render(); });
  }
})();
