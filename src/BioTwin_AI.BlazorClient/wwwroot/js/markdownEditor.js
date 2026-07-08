(function () {
    const editors = new WeakMap();

    function attachTextareaFallback(textarea, dotNetReference) {
        const handler = function () {
            dotNetReference.invokeMethodAsync("NotifyMarkdownChanged", textarea.value || "");
        };
        textarea.addEventListener("input", handler);
        return {
            setValue(value) {
                textarea.value = value || "";
            },
            dispose() {
                textarea.removeEventListener("input", handler);
            }
        };
    }

    window.markdownEditor = {
        initialize(textarea, value, dotNetReference) {
            if (!textarea) {
                return;
            }

            textarea.value = value || "";

            if (window.EasyMDE) {
                const easyMde = new window.EasyMDE({
                    element: textarea,
                    initialValue: value || "",
                    autofocus: false,
                    spellChecker: false,
                    status: false,
                    minHeight: "460px",
                    toolbar: [
                        "heading",
                        "bold",
                        "italic",
                        "|",
                        "unordered-list",
                        "ordered-list",
                        "|",
                        "quote",
                        "code",
                        "|",
                        "preview",
                        "side-by-side",
                        "fullscreen"
                    ]
                });

                easyMde.codemirror.on("change", function () {
                    dotNetReference.invokeMethodAsync("NotifyMarkdownChanged", easyMde.value() || "");
                });

                editors.set(textarea, {
                    setValue(nextValue) {
                        if ((easyMde.value() || "") !== (nextValue || "")) {
                            easyMde.value(nextValue || "");
                        }
                    },
                    dispose() {
                        easyMde.toTextArea();
                    }
                });
                return;
            }

            editors.set(textarea, attachTextareaFallback(textarea, dotNetReference));
        },

        setValue(textarea, value) {
            const editor = editors.get(textarea);
            if (editor) {
                editor.setValue(value || "");
            } else if (textarea) {
                textarea.value = value || "";
            }
        },

        dispose(textarea) {
            const editor = editors.get(textarea);
            if (editor) {
                editor.dispose();
                editors.delete(textarea);
            }
        }
    };
})();
