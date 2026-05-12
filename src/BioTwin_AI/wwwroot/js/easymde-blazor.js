(function () {
    const editors = new Map();
    function buildToolbar(readOnly) {
        if (readOnly) {
            return false;
        }

        return [
            "bold",
            "italic",
            "heading",
            "|",
            "quote",
            "unordered-list",
            "ordered-list",
            "|",
            "link",
            "table",
            "code",
            "|",
            "preview",
            "fullscreen",
            "|",
            "undo",
            "redo",
            "|",
            "guide"
        ];
    }

    function setNativeFallback(element, options) {
        element.value = options.value || "";
        element.placeholder = options.placeholder || "";
        element.readOnly = !!options.readOnly;
        element.rows = options.rows || 14;
        element.addEventListener("input", () => {
            options.dotNetRef.invokeMethodAsync("OnEditorValueChanged", element.value);
        });
    }

    window.bioTwinMarkdownEditor = {
        async init(id, dotNetRef, options) {
            const element = document.getElementById(id);
            if (!element) {
                return;
            }

            const editorOptions = {
                value: options.value || "",
                placeholder: options.placeholder || "",
                readOnly: !!options.readOnly,
                rows: options.rows || 14,
                dotNetRef
            };

            if (!window.EasyMDE) {
                console.warn("EasyMDE is not loaded. Falling back to native textarea.");
                setNativeFallback(element, editorOptions);
                return;
            }

            const minHeight = `${Math.max(260, editorOptions.rows * 28)}px`;
            const editor = new window.EasyMDE({
                element,
                initialValue: editorOptions.value,
                placeholder: editorOptions.placeholder,
                autofocus: false,
                autoDownloadFontAwesome: false,
                forceSync: true,
                spellChecker: false,
                autoRefresh: { delay: 300 },
                status: editorOptions.readOnly ? false : ["lines", "words", "cursor"],
                minHeight,
                toolbar: buildToolbar(editorOptions.readOnly),
                toolbarTips: true,
                renderingConfig: {
                    singleLineBreaks: false,
                    codeSyntaxHighlighting: false
                }
            });

            editor.codemirror.setOption("readOnly", editorOptions.readOnly ? "nocursor" : false);

            let lastValue = editorOptions.value;
            let debounceTimer = null;
            editor.codemirror.on("change", () => {
                const value = editor.value();
                if (value === lastValue) {
                    return;
                }

                lastValue = value;
                window.clearTimeout(debounceTimer);
                debounceTimer = window.setTimeout(() => {
                    dotNetRef.invokeMethodAsync("OnEditorValueChanged", value);
                }, 120);
            });

            editors.set(id, {
                editor,
                lastValue,
                dotNetRef
            });
        },

        setOptions(id, options) {
            const state = editors.get(id);
            if (!state) {
                const element = document.getElementById(id);
                if (element && typeof options.value === "string" && element.value !== options.value) {
                    element.value = options.value;
                }
                if (element && typeof options.readOnly === "boolean") {
                    element.readOnly = options.readOnly;
                }
                return;
            }

            if (typeof options.value === "string" && state.editor.value() !== options.value) {
                state.lastValue = options.value;
                state.editor.value(options.value);
            }

            if (typeof options.readOnly === "boolean") {
                state.editor.codemirror.setOption("readOnly", options.readOnly ? "nocursor" : false);
            }
        },

        dispose(id) {
            const state = editors.get(id);
            if (!state) {
                return;
            }

            state.editor.toTextArea();
            editors.delete(id);
        }
    };
})();
