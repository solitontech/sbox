window.sbox = window.sbox || {};

window.sbox.scrollToEnd = (element) => {
    if (!element) {
        return;
    }

    element.scrollTop = element.scrollHeight;
};
