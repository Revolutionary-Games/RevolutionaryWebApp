// A real textarea preserves plain-text editing, selection, undo, and paste behavior.
// A matching backdrop paints only mention backgrounds; all user text uses textContent.
export function create(root, receiver, value, mentionPattern) {
    const input = root.querySelector('textarea');
    const highlights = root.querySelector('.mention-highlights');
    const popup = root.querySelector('.mention-suggestions');
    const pattern = new RegExp(mentionPattern, 'g');
    let known = new Map();
    let suggestions = [];
    let selected = 0;
    let active = null;
    let disposed = false;
    let lastRequest = '';
    let dismissedState = '';
    let handledKey = false;
    input.value = value;

    function paint() {
        const fragment = document.createDocumentFragment();
        let offset = 0;
        for (const match of input.value.matchAll(pattern)) {
            fragment.append(document.createTextNode(input.value.slice(offset, match.index)));
            const mark = document.createElement('mark');
            mark.textContent = match[0];
            // Unknown names stay neutral until the server has checked them.
            if (!known.has(match[1])) mark.style.background = 'transparent';
            else if (!known.get(match[1])) mark.className = 'invalid';
            fragment.append(mark);
            offset = match.index + match[0].length;
        }
        fragment.append(document.createTextNode(input.value.slice(offset) + '\n'));
        highlights.replaceChildren(fragment);
        syncScroll();
    }

    function syncScroll() {
        // Account for scrollbar width so wrapped lines match the textarea exactly.
        highlights.style.width = `${input.clientWidth + 2}px`;
        highlights.scrollTop = input.scrollTop;
        highlights.scrollLeft = input.scrollLeft;
        if (!popup.hidden) positionPopup();
    }

    function currentMention() {
        if (input.selectionStart !== input.selectionEnd) return null;
        const before = input.value.slice(0, input.selectionStart);
        const match = /(?<!\w)@([A-Za-z0-9_-]{0,50})$/.exec(before);
        if (!match) return null;
        const suffix = /^[A-Za-z0-9_-]*/.exec(input.value.slice(input.selectionStart))[0];
        if (match[1].length + suffix.length > 50) return null;
        return { start: match.index, end: input.selectionStart + suffix.length, query: match[1] };
    }

    function hide() {
        popup.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
    }

    function stateKey() {
        return JSON.stringify([input.value, input.selectionStart]);
    }

    function dismiss() {
        dismissedState = stateKey();
        hide();
    }

    function notify() {
        if (disposed) return;
        paint();
        active = currentMention();
        const query = active?.query ?? null;
        const request = JSON.stringify([input.value, query]);
        if (request === lastRequest) {
            if (!popup.hidden) positionPopup();
            return;
        }
        hide();
        lastRequest = request;
        receiver.invokeMethodAsync('EditorChanged', input.value, query).catch(() => {
            // The component may have been disposed while an interop callback was queued.
        });
    }

    function positionPopup() {
        const mirror = document.createElement('div');
        const style = getComputedStyle(input);
        for (const property of ['font', 'lineHeight', 'letterSpacing', 'padding', 'border',
            'boxSizing', 'whiteSpace', 'overflowWrap', 'tabSize']) mirror.style[property] = style[property];
        Object.assign(mirror.style, { position: 'absolute', visibility: 'hidden',
            width: `${input.clientWidth + 2}px`, whiteSpace: 'pre-wrap', overflowWrap: 'break-word',
            left: '0', top: '0' });
        mirror.textContent = input.value.slice(0, input.selectionStart);
        const caret = document.createElement('span');
        caret.textContent = '\u200b';
        mirror.append(caret);
        root.append(mirror);
        const caretRect = caret.getBoundingClientRect();
        const rootRect = root.getBoundingClientRect();
        const top = caretRect.bottom - rootRect.top - input.scrollTop;
        popup.style.left = `${Math.max(0, Math.min(caretRect.left - rootRect.left - input.scrollLeft,
            root.clientWidth - popup.offsetWidth))}px`;
        popup.style.top = `${Math.max(0, Math.min(top, input.clientHeight))}px`;
        mirror.remove();
    }

    function choose(index) {
        if (!active || !suggestions[index]) return;
        const replacement = '@' + suggestions[index];
        input.focus();
        input.setRangeText(replacement, active.start, active.end, 'end');
        hide();
        // Keep typing after the chosen mention rather than opening the list again.
        notify();
        dismiss();
    }

    function renderSuggestions() {
        popup.replaceChildren();
        suggestions.forEach((name, index) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.id = `${popup.id}-${index}`;
            button.setAttribute('role', 'option');
            button.setAttribute('aria-selected', String(index === selected));
            button.tabIndex = -1;
            button.textContent = '@' + name;
            button.addEventListener('pointerdown', event => { event.preventDefault(); choose(index); });
            popup.append(button);
        });
        popup.hidden = suggestions.length === 0;
        input.setAttribute('aria-expanded', String(!popup.hidden));
        if (!popup.hidden) {
            input.setAttribute('aria-activedescendant', `${popup.id}-${selected}`);
            positionPopup();
            popup.children[selected]?.scrollIntoView({ block: 'nearest' });
        }
    }

    function keydown(event) {
        if (popup.hidden || event.isComposing) return;
        handledKey = ['Escape', 'ArrowDown', 'ArrowUp', 'Enter', 'Tab'].includes(event.key);
        if (event.key === 'Escape') { event.preventDefault(); dismiss(); }
        else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            selected = (selected + (event.key === 'ArrowDown' ? 1 : -1) + suggestions.length) % suggestions.length;
            renderSuggestions();
        } else if (event.key === 'Enter' || event.key === 'Tab') {
            event.preventDefault();
            choose(selected);
        }
    }

    function clicked() {
        dismissedState = '';
        lastRequest = '';
        notify();
    }

    function selectionChanged(_event) {
        if (handledKey) { handledKey = false; return; }
        notify();
    }

    input.addEventListener('input', notify);
    input.addEventListener('click', clicked);
    input.addEventListener('keyup', selectionChanged);
    input.addEventListener('keydown', keydown);
    input.addEventListener('blur', hide);
    input.addEventListener('scroll', syncScroll);
    const resize = new ResizeObserver(() => { paint(); });
    resize.observe(input);
    paint();


    return {
        update(validNames, matches, query) {
            if (disposed) return;
            known = new Map(Array.from(input.value.matchAll(pattern), m => [m[1], validNames.includes(m[1])]));
            paint();
            active = currentMention();
            if (document.activeElement !== input || !active || active.query !== query ||
                query?.length < 3 || dismissedState === stateKey()) return;
            suggestions = matches;
            selected = 0;
            renderSuggestions();
        },
        setValue(text) {
            if (text === input.value) return;
            input.value = text;
            known.clear();
            lastRequest = '';
            notify();
        },
        dispose() {
            disposed = true;
            resize.disconnect();
            input.removeEventListener('input', notify);
            input.removeEventListener('click', clicked);
            input.removeEventListener('keyup', selectionChanged);
            input.removeEventListener('keydown', keydown);
            input.removeEventListener('blur', hide);
            input.removeEventListener('scroll', syncScroll);
        }
    };
}
