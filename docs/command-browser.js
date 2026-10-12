'use strict';

/*
 * The command catalog is built from docs/COMMANDS.md, the project's existing
 * canonical command reference. Do not duplicate commands or prices here.
 */
(function () {
  const sectionLabels = {
    gameplay: 'Game Commands',
    minigame: 'Minigame Commands',
    utility: 'Utility Commands',
    streamer: 'Streamer Commands'
  };
  const sectionOrder = ['gameplay', 'minigame', 'utility', 'streamer'];
  const validCategories = new Set(['all', ...sectionOrder]);
  const searchInput = document.getElementById('command-search');
  const freeOnlyInput = document.getElementById('free-only');
  const multiplierSelect = document.getElementById('price-multiplier');
  const categoryRow = document.getElementById('category-row');
  const resultsElement = document.getElementById('command-results');
  const resultLabel = document.getElementById('results-label');
  const clearButton = document.getElementById('clear-filters');
  const emptyState = document.getElementById('empty-state');
  const errorState = document.getElementById('load-error');
  const toast = document.getElementById('copy-status');
  const params = new URLSearchParams(window.location.search);
  const initialCategory = params.get('category') || 'all';

  let commands = [];
  let category = validCategories.has(initialCategory) ? initialCategory : 'all';
  let toastTimer = 0;
  searchInput.value = (params.get('q') || '').slice(0, 150);
  freeOnlyInput.checked = params.get('free') === '1';
  const requestedMultiplier = params.get('multiplier');

  function make(tag, className, value) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (value !== undefined) node.textContent = value;
    return node;
  }

  // Markdown table cells may include backtick code spans and escaped pipes.
  // Avoid splitting on pipes that are part of commands such as <user|all|random>.
  function splitRow(line) {
    const trimmed = line.trim();
    if (!trimmed.startsWith('|')) return [];
    let raw = trimmed.slice(1);
    if (raw.endsWith('|')) raw = raw.slice(0, -1);
    const cells = [];
    let cell = '';
    let inCode = false;
    for (let i = 0; i < raw.length; i++) {
      const character = raw[i];
      if (character === '\\' && i + 1 < raw.length && raw[i + 1] === '|') {
        cell += '|';
        i++;
      } else if (character.charCodeAt(0) === 96) {
        inCode = !inCode;
        cell += character;
      } else if (character === '|' && !inCode) {
        cells.push(cell.trim());
        cell = '';
      } else {
        cell += character;
      }
    }
    cells.push(cell.trim());
    return cells;
  }

  function plainText(value) {
    return value
      .replace(/\x60/g, '')
      .replace(/\\\|/g, '|')
      .replace(/\\([\\*_])/g, '$1')
      .trim();
  }

  function parseCommands(markdown) {
    let section = null;
    const seen = new Set();
    const parsed = [];

    for (const line of markdown.split(/\r?\n/)) {
      if (/^#{2,3} /.test(line)) {
        const title = line.replace(/^#{2,3} /, '').trim().toLowerCase();
        section = title === 'game commands' || title === 'gameplay commands'
          ? 'gameplay'
          : title === 'minigame commands'
            ? 'minigame'
            : title === 'utility commands' || title === 'utility and economy'
              ? 'utility'
              : title === 'streamer commands'
                ? 'streamer'
                : null;
      }
      if (!section || !/^\|\s*\x60![a-z0-9]+\x60\s*\|/i.test(line)) continue;

      const fields = splitRow(line).map(plainText);
      if (fields.length !== 6) {
        throw new Error('Unexpected command table format for ' + fields[0]);
      }
      const [name, syntax, cost, targeting, permission, description] = fields;
      if (!/^![a-z0-9]+$/i.test(name) || !syntax.startsWith(name)) {
        throw new Error('Invalid command syntax for ' + name);
      }
      if (seen.has(name.toLowerCase())) {
        throw new Error('Duplicate command in documentation: ' + name);
      }
      seen.add(name.toLowerCase());
      parsed.push({ name, syntax, cost, targeting, permission, description, section });
    }

    // Fail loudly instead of silently showing just one section if headings change.
    const documentedRows = (markdown.match(/^\|\s*`![a-z0-9]+`\s*\|/gim) || []).length;
    if (!parsed.length || parsed.length !== documentedRows) {
      throw new Error('Command documentation mismatch: parsed ' + parsed.length + ' of ' + documentedRows + ' commands.');
    }
    return parsed.sort((a, b) => a.name.localeCompare(b.name));
  }

  function showToast(message) {
    window.clearTimeout(toastTimer);
    toast.textContent = message;
    toast.hidden = false;
    toastTimer = window.setTimeout(() => { toast.hidden = true; }, 2500);
  }

  async function copyToClipboard(value) {
    if (navigator.clipboard && window.isSecureContext) {
      try {
        await navigator.clipboard.writeText(value);
        return true;
      } catch {
        // Fall through for browsers that deny the Clipboard API.
      }
    }
    const textarea = make('textarea');
    textarea.value = value;
    textarea.readOnly = true;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    let copied = false;
    try {
      copied = document.execCommand('copy');
    } catch {
      copied = false;
    } finally {
      textarea.remove();
    }
    return copied;
  }

  // The desktop app's preset list is the source of truth. Fetch it from the
  // public main branch so the website picks up future preset changes without
  // maintaining a separate preset array in the frontend.
  const presetSource = 'https://raw.githubusercontent.com/AntnyGamer/TwitchCraft/main/TwitchCraft_SOURCE/Frames/Settings.xaml.cs';

  function parseMultiplierOptions(source) {
    const definition = source.match(/CommandCostMultiplierOptions\s*=\s*\[([\s\S]*?)\]/);
    if (!definition) throw new Error('Multiplier presets were not found in Settings.xaml.cs');
    const options = Array.from(definition[1].matchAll(/\(\s*(\d+(?:\.\d+)?)\s*,\s*"([^"]+)"\s*\)/g),
      match => ({ value: String(Number(match[1])), label: match[2].replace(/x$/i, '×') }));
    if (!options.length || !options.some(item => item.value === '1')
        || options.some(item => Number(item.value) < 0 || Number(item.value) > 5)) {
      throw new Error('Multiplier preset values are invalid');
    }
    return options;
  }

  async function refreshMultiplierOptions() {
    // The HTML keeps the exact currently-supported presets as an offline
    // fallback; an unavailable GitHub host must not break the command list.
    try {
      const response = await fetch(presetSource);
      if (!response.ok) throw new Error('HTTP ' + response.status);
      const presets = parseMultiplierOptions(await response.text());
      multiplierSelect.replaceChildren();
      for (const option of presets) {
        const element = document.createElement('option');
        element.value = option.value;
        element.textContent = option.label;
        multiplierSelect.appendChild(element);
      }
    } catch (error) {
      console.warn('Using local multiplier presets:', error);
    }
    if (requestedMultiplier && Array.from(multiplierSelect.options).some(option => option.value === requestedMultiplier)) {
      multiplierSelect.value = requestedMultiplier;
    } else {
      multiplierSelect.value = Array.from(multiplierSelect.options).some(option => option.value === '1')
        ? '1' : multiplierSelect.options[0].value;
    }
  }

  function displayCost(item) {
    const source = item.cost.trim();
    const originalFree = source.toLowerCase().startsWith('free');
    const multiplier = Number(multiplierSelect.value);

    // The command-cost multiplier belongs to gameplay command charging.
    // Economy transfers, wagers and admin commands use their own amounts.
    if (item.section !== 'gameplay' || originalFree) {
      return { label: /^\d+$/.test(source) ? source + ' tokens' : source, free: originalFree };
    }
    if (multiplier === 0) return { label: 'Free (0 tokens)', free: true };

    // Mirrors TwitchCraft CommandService.GetCommandCost: round UP to integer.
    const scale = amount => Math.ceil(amount * multiplier);
    if (/^\d+$/.test(source)) {
      return { label: scale(Number(source)) + ' tokens', free: false };
    }
    const perUnit = source.match(/^(\d+) per (heart|effect)$/i);
    if (perUnit) {
      const base = Number(perUnit[1]);
      const maximum = perUnit[2].toLowerCase() === 'heart' ? 5 : 25;
      const unit = maximum === 5 ? 'hearts' : 'effects';
      return { label: scale(base) + '–' + scale(base * maximum) + ' tokens (1–' + maximum + ' ' + unit + ')', free: false };
    }
    return { label: source, free: false };
  }

  function createCommandCard(item) {
    const card = make('article', 'command-card');
    const top = make('div', 'card-top');
    const name = make('h4', 'command-name', item.name);
    const priceInfo = displayCost(item);
    const price = make('span', priceInfo.free ? 'cost is-free' : 'cost', 'Cost: ' + priceInfo.label);
    top.append(name, price);

    const description = make('p', 'card-description', item.description);
    const syntax = make('code', 'syntax', item.syntax);
    const meta = make('div', 'card-meta');
    meta.append(
      make('span', 'meta-tag', item.targeting),
      make('span', item.permission === 'Everyone' ? 'meta-tag' : 'meta-tag admin', item.permission)
    );
    const copy = make('button', 'copy-button');
    copy.type = 'button';
    const copyLabel = make('span', '', 'Copy syntax');
    const copyGlyph = make('span', '', '↗');
    copyGlyph.setAttribute('aria-hidden', 'true');
    copy.append(copyLabel, copyGlyph);
    copy.setAttribute('aria-label', 'Copy syntax for ' + item.name);
    copy.addEventListener('click', async () => {
      const success = await copyToClipboard(item.syntax);
      if (success) {
        copyLabel.textContent = 'Copied!';
        copy.classList.add('copied');
        showToast('Copied ' + item.name + ' syntax. Replace placeholders before sending.');
        window.setTimeout(() => {
          copyLabel.textContent = 'Copy syntax';
          copy.classList.remove('copied');
        }, 1400);
      } else {
        showToast('Clipboard blocked. Select the syntax shown on the card to copy.');
      }
    });
    card.append(top, description, syntax, meta, copy);
    return card;
  }

  function updateCategoryButtons() {
    const counts = { all: commands.length, gameplay: 0, minigame: 0, utility: 0, streamer: 0 };
    for (const item of commands) counts[item.section]++;
    for (const button of categoryRow.querySelectorAll('[data-category]')) {
      const active = button.dataset.category === category;
      button.classList.toggle('is-selected', active);
      button.setAttribute('aria-pressed', String(active));
      const countElement = button.querySelector('[data-count]');
      if (countElement) countElement.textContent = String(counts[button.dataset.category]);
    }
    document.getElementById('total-count').textContent = String(counts.all);
    document.getElementById('gameplay-count').textContent = String(counts.gameplay);
    document.getElementById('utility-count').textContent = String(counts.utility);
    document.getElementById('minigame-count').textContent = String(counts.minigame);
    document.getElementById('streamer-count').textContent = String(counts.streamer);
  }

  function syncAddress() {
    const next = new URL(window.location.href);
    const query = searchInput.value.trim();
    if (query) next.searchParams.set('q', query);
    else next.searchParams.delete('q');
    if (category !== 'all') next.searchParams.set('category', category);
    else next.searchParams.delete('category');
    if (freeOnlyInput.checked) next.searchParams.set('free', '1');
    else next.searchParams.delete('free');
    if (multiplierSelect.value !== '1') next.searchParams.set('multiplier', multiplierSelect.value);
    else next.searchParams.delete('multiplier');
    window.history.replaceState(null, '', next.pathname + next.search + next.hash);
  }

  function render() {
    const query = searchInput.value.toLocaleLowerCase().trim();
    const onlyFree = freeOnlyInput.checked;
    const filtered = commands.filter(item => {
      if (category !== 'all' && item.section !== category) return false;
      if (onlyFree && !displayCost(item).free) return false;
      if (!query) return true;
      return [item.name, item.syntax, item.cost, displayCost(item).label, item.targeting, item.permission,
        item.description, sectionLabels[item.section]].some(field => field.toLocaleLowerCase().includes(query));
    });
    const fragment = document.createDocumentFragment();
    for (const section of sectionOrder) {
      const list = filtered.filter(item => item.section === section);
      if (!list.length) continue;
      const wrapper = make('section', 'command-group');
      wrapper.setAttribute('aria-label', sectionLabels[section]);
      const heading = make('div', 'group-heading');
      heading.append(make('h3', '', sectionLabels[section]), make('span', '', list.length + (list.length === 1 ? ' command' : ' commands')));
      const grid = make('div', 'command-grid');
      for (const item of list) grid.appendChild(createCommandCard(item));
      wrapper.append(heading, grid);
      fragment.appendChild(wrapper);
    }
    resultsElement.replaceChildren(fragment);
    resultLabel.textContent = 'Showing ' + filtered.length + ' of ' + commands.length + (commands.length === 1 ? ' command' : ' commands');
    emptyState.hidden = filtered.length > 0;
    clearButton.hidden = !query && category === 'all' && !onlyFree;
    updateCategoryButtons();
    syncAddress();
  }

  function clearFilters() {
    searchInput.value = '';
    freeOnlyInput.checked = false;
    category = 'all';
    render();
  }

  function wireEvents() {
    searchInput.addEventListener('input', render);
    freeOnlyInput.addEventListener('change', render);
    multiplierSelect.addEventListener('change', render);
    categoryRow.addEventListener('click', event => {
      const button = event.target.closest('[data-category]');
      if (!button || !categoryRow.contains(button)) return;
      category = validCategories.has(button.dataset.category) ? button.dataset.category : 'all';
      render();
    });
    clearButton.addEventListener('click', clearFilters);
    document.getElementById('empty-reset').addEventListener('click', clearFilters);
    document.addEventListener('keydown', event => {
      const element = document.activeElement;
      const typing = element && (element.tagName === 'INPUT' || element.tagName === 'TEXTAREA' || element.isContentEditable);
      if (event.key === '/' && !event.altKey && !event.ctrlKey && !event.metaKey && !typing) {
        event.preventDefault();
        searchInput.focus();
      }
    });
  }

  async function initialize() {
    wireEvents();
    await refreshMultiplierOptions();
    try {
      const response = await fetch('../COMMANDS.md', { cache: 'no-cache' });
      if (!response.ok) throw new Error('HTTP ' + response.status);
      const markdown = await response.text();
      if (/^\s*<!doctype html/i.test(markdown) || /^\s*<html/i.test(markdown)) {
        throw new Error('GitHub Pages transformed COMMANDS.md to HTML. Ensure docs/.nojekyll exists.');
      }
      commands = parseCommands(markdown);
      render();
      resultsElement.setAttribute('aria-busy', 'false');
    } catch (error) {
      console.error('Unable to load TwitchCraft command catalog:', error);
      resultsElement.setAttribute('aria-busy', 'false');
      resultLabel.textContent = 'Command list unavailable';
      errorState.hidden = false;
    }
  }

  initialize();
})();
