window.monacoEditorInitialized = false;
window._monacoEditor = null;

window.initMonaco = function (containerId, initialCode, dotNetRef) {
    if (dotNetRef) window._dotNetDebugRef = dotNetRef;
    require.config({
        paths: { 'vs': 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.0/min/vs' }
    });
    require(['vs/editor/editor.main'], function () {
        monaco.languages.register({ id: 'hlsl' });
        monaco.languages.setMonarchTokensProvider('hlsl', {
            // Control flow
            controlKeywords: [
                'if', 'else', 'for', 'while', 'do', 'switch', 'case', 'default',
                'break', 'continue', 'return', 'discard',
            ],
            // Storage class / interpolation modifiers
            modifierKeywords: [
                'static', 'const', 'uniform', 'in', 'out', 'inout', 'inline',
                'extern', 'shared', 'groupshared', 'globallycoherent', 'volatile',
                'precise', 'nointerpolation', 'noperspective', 'centroid', 'linear',
                'row_major', 'column_major', 'snorm', 'unorm', 'unsigned',
                'export', 'indices', 'vertices', 'primitives', 'payload',
            ],
            // Scalar / vector / matrix types
            typeKeywords: [
                'void', 'bool', 'int', 'uint', 'dword', 'half', 'float', 'double',
                'string', 'vector', 'matrix',
                'bool1','bool2','bool3','bool4',
                'int1','int2','int3','int4',
                'uint1','uint2','uint3','uint4',
                'half1','half2','half3','half4',
                'float1','float2','float3','float4',
                'double1','double2','double3','double4',
                'bool1x1','bool1x2','bool1x3','bool1x4',
                'bool2x1','bool2x2','bool2x3','bool2x4',
                'bool3x1','bool3x2','bool3x3','bool3x4',
                'bool4x1','bool4x2','bool4x3','bool4x4',
                'int1x1','int1x2','int1x3','int1x4',
                'int2x1','int2x2','int2x3','int2x4',
                'int3x1','int3x2','int3x3','int3x4',
                'int4x1','int4x2','int4x3','int4x4',
                'uint1x1','uint1x2','uint1x3','uint1x4',
                'uint2x1','uint2x2','uint2x3','uint2x4',
                'uint3x1','uint3x2','uint3x3','uint3x4',
                'uint4x1','uint4x2','uint4x3','uint4x4',
                'half1x1','half1x2','half1x3','half1x4',
                'half2x1','half2x2','half2x3','half2x4',
                'half3x1','half3x2','half3x3','half3x4',
                'half4x1','half4x2','half4x3','half4x4',
                'float1x1','float1x2','float1x3','float1x4',
                'float2x1','float2x2','float2x3','float2x4',
                'float3x1','float3x2','float3x3','float3x4',
                'float4x1','float4x2','float4x3','float4x4',
                'double1x1','double1x2','double1x3','double1x4',
                'double2x1','double2x2','double2x3','double2x4',
                'double3x1','double3x2','double3x3','double3x4',
                'double4x1','double4x2','double4x3','double4x4',
                'min16float','min16float1','min16float2','min16float3','min16float4',
                'min16int','min16int1','min16int2','min16int3','min16int4',
                'min16uint','min16uint1','min16uint2','min16uint3','min16uint4',
                'min12int','min12int1','min12int2','min12int3','min12int4',
                'min10float','min10float1','min10float2','min10float3','min10float4',
            ],
            // Struct / resource / object types (PascalCase)
            objectKeywords: [
                'struct', 'class', 'interface', 'typedef', 'namespace',
                'cbuffer', 'tbuffer', 'technique', 'technique10', 'technique11', 'pass',
                'SamplerState', 'SamplerComparisonState',
                'sampler', 'sampler1D', 'sampler2D', 'sampler3D', 'samplerCUBE',
                'Texture', 'Texture2DLegacy', 'TextureCubeLegacy',
                'Texture1D', 'Texture1DArray',
                'Texture2D', 'Texture2DArray', 'Texture2DMS', 'Texture2DMSArray',
                'Texture3D', 'TextureCube', 'TextureCubeArray',
                'RWTexture1D', 'RWTexture1DArray',
                'RWTexture2D', 'RWTexture2DArray',
                'RWTexture3D',
                'Buffer', 'ByteAddressBuffer', 'StructuredBuffer',
                'RWBuffer', 'RWByteAddressBuffer', 'RWStructuredBuffer',
                'AppendStructuredBuffer', 'ConsumeStructuredBuffer',
                'RasterizerOrderedBuffer', 'RasterizerOrderedByteAddressBuffer',
                'RasterizerOrderedStructuredBuffer',
                'RasterizerOrderedTexture1D', 'RasterizerOrderedTexture1DArray',
                'RasterizerOrderedTexture2D', 'RasterizerOrderedTexture2DArray',
                'RasterizerOrderedTexture3D',
                'InputPatch', 'OutputPatch',
                'LineStream', 'TriangleStream', 'PointStream',
                'BlendState', 'DepthStencilState', 'RasterizerState',
            ],
            // Literals
            literalKeywords: ['true', 'false', 'NULL'],
            // Built-in intrinsic functions
            builtins: [
                'abs','acos','all','any','asdouble','asfloat','asin','asint','asuint',
                'atan','atan2','ceil','clamp','clip','cos','cosh','countbits','cross',
                'D3DCOLORtoUBYTE4','ddx','ddx_coarse','ddx_fine','ddy','ddy_coarse','ddy_fine',
                'degrees','determinant','distance','dot','dst','exp','exp2',
                'f16tof32','f32tof16','faceforward','firstbithigh','firstbitlow',
                'floor','fma','fmod','frac','frexp','fwidth',
                'isfinite','isinf','isnan','ldexp','length','lerp','lit',
                'log','log10','log2','mad','max','min','modf','msad4','mul',
                'noise','normalize','pow','radians','rcp','reflect','refract',
                'reversebits','round','rsqrt','saturate','sign','sin','sincos','sinh',
                'smoothstep','sqrt','step','tan','tanh','transpose','trunc',
                'printf','errorf','abort',
                'AllMemoryBarrier','DeviceMemoryBarrier','GroupMemoryBarrier',
                'AllMemoryBarrierWithGroupSync','DeviceMemoryBarrierWithGroupSync','GroupMemoryBarrierWithGroupSync',
                'GetRenderTargetSampleCount','GetRenderTargetSamplePosition',
                'QuadReadAcrossDiagonal','QuadReadLaneAt','QuadReadAcrossX','QuadReadAcrossY',
                'WaveActiveAllEqual','WaveActiveBitAnd','WaveActiveBitOr','WaveActiveBitXor',
                'WaveActiveCountBits','WaveActiveMax','WaveActiveMin','WaveActiveProduct',
                'WaveActiveSum','WaveActiveAllTrue','WaveActiveAnyTrue','WaveActiveBallot',
                'WaveGetLaneCount','WaveGetLaneIndex','WaveIsFirstLane',
                'WavePrefixCountBits','WavePrefixProduct','WavePrefixSum',
                'WaveReadLaneFirst','WaveReadLaneAt',
                // Test runner helpers
                'ASSERT', 'ASSERT_EQUAL', 'ASSERT_NEAR', 'ASSERT_UNIFORM',
                'ASSERT_VARYING', 'ASSERT_MSG', 'PASS_TEST', 'FAIL_TEST',
                'IGNORE_TEST', 'PRINTF', 'MOCK_RESOURCE',
                'TEST_NAME', 'TEST_CASE', 'TEST_VALUE',
                'PASS_TEST_MSG','IGNORE_TEST_MSG','FAIL_TEST_MSG',
            ],

            tokenizer: {
                root: [
                    // Preprocessor directives
                    [/^\s*#\s*\w+/, 'keyword.directive'],
                    // Annotations [Attribute]
                    [/\[/, { token: 'annotation.bracket', next: '@annotation' }],
                    // Numbers
                    [/\d*\.\d+([eE][\-+]?\d+)?[fFhH]?/, 'number.float'],
                    [/\d+[fFhH]/, 'number.float'],
                    [/0[xX][0-9a-fA-F]+[uU]?/, 'number.hex'],
                    [/\d+[uU]?/, 'number'],
                    // Strings
                    [/"([^"\\]|\\.)*"/, 'string'],
                    // Identifiers & keywords
                    [/[a-zA-Z_]\w*/, {
                        cases: {
                            '@controlKeywords': 'keyword.control',
                            '@modifierKeywords': 'keyword.modifier',
                            '@typeKeywords':    'keyword.type',
                            '@objectKeywords':  'keyword.type',
                            '@literalKeywords': 'keyword.literal',
                            '@builtins':        'support.function',
                            '@default':         'identifier',
                        }
                    }],
                    // Whitespace & comments
                    { include: '@whitespace' },
                    // Brackets
                    [/[{}()\[\]]/, '@brackets'],
                    // Operators / punctuation
                    [/[=!<>+\-*\/%&|^~?:;,.]/, 'operator'],
                ],
                annotation: [
                    [/\]/, { token: 'annotation.bracket', next: '@pop' }],
                    [/[a-zA-Z_]\w*/, 'annotation'],
                    [/[(),]/, 'annotation'],
                    [/\d+/, 'annotation'],
                    [/"([^"\\]|\\.)*"/, 'annotation'],
                ],
                whitespace: [
                    [/[ \t\r\n]+/, ''],
                    [/\/\*/, 'comment', '@comment'],
                    [/\/\/.*$/, 'comment'],
                ],
                comment: [
                    [/[^\/*]+/, 'comment'],
                    [/\/\*/, 'comment', '@push'],
                    [/\*\//, 'comment', '@pop'],
                    [/[\/*]/, 'comment'],
                ],
            },
        });

        // Auto-complete for HLSL intrinsics
        (function () {
            const intrinsics = [
                'abs','acos','all','any','asdouble','asfloat','asin','asint','asuint',
                'atan','atan2','ceil','clamp','clip','cos','cosh','countbits','cross',
                'D3DCOLORtoUBYTE4','ddx','ddx_coarse','ddx_fine','ddy','ddy_coarse','ddy_fine',
                'degrees','determinant','distance','dot','dst','exp','exp2',
                'f16tof32','f32tof16','faceforward','firstbithigh','firstbitlow',
                'floor','fma','fmod','frac','frexp','fwidth',
                'isfinite','isinf','isnan','ldexp','length','lerp','lit',
                'log','log10','log2','mad','max','min','modf','msad4','mul',
                'noise','normalize','pow','radians','rcp','reflect','refract',
                'reversebits','round','rsqrt','saturate','sign','sin','sincos','sinh',
                'smoothstep','sqrt','step','tan','tanh','transpose','trunc',
                'printf','errorf','abort',
                'AllMemoryBarrier','DeviceMemoryBarrier','GroupMemoryBarrier',
                'AllMemoryBarrierWithGroupSync','DeviceMemoryBarrierWithGroupSync','GroupMemoryBarrierWithGroupSync',
                'GetRenderTargetSampleCount','GetRenderTargetSamplePosition',
                'QuadReadAcrossDiagonal','QuadReadLaneAt','QuadReadAcrossX','QuadReadAcrossY',
                'WaveActiveAllEqual','WaveActiveBitAnd','WaveActiveBitOr','WaveActiveBitXor',
                'WaveActiveCountBits','WaveActiveMax','WaveActiveMin','WaveActiveProduct',
                'WaveActiveSum','WaveActiveAllTrue','WaveActiveAnyTrue','WaveActiveBallot',
                'WaveGetLaneCount','WaveGetLaneIndex','WaveIsFirstLane',
                'WavePrefixCountBits','WavePrefixProduct','WavePrefixSum',
                'WaveReadLaneFirst','WaveReadLaneAt',
                'ASSERT', 'ASSERT_EQUAL', 'ASSERT_NEAR', 'ASSERT_UNIFORM',
                'ASSERT_VARYING', 'ASSERT_MSG', 'PASS_TEST', 'FAIL_TEST',
                'IGNORE_TEST', 'PRINTF', 'MOCK_RESOURCE',
                'TEST_NAME', 'TEST_CASE', 'TEST_VALUE',
                'PASS_TEST_MSG', 'IGNORE_TEST_MSG', 'FAIL_TEST_MSG',
            ];
            // Intrinsics that take no arguments (no cursor inside parens needed)
            const noArgIntrinsics = new Set([
                'AllMemoryBarrier','DeviceMemoryBarrier','GroupMemoryBarrier',
                'AllMemoryBarrierWithGroupSync','DeviceMemoryBarrierWithGroupSync','GroupMemoryBarrierWithGroupSync',
                'GetRenderTargetSampleCount','WaveGetLaneCount','WaveGetLaneIndex','WaveIsFirstLane',
                'abort','PASS_TEST','FAIL_TEST',
            ]);
            const intrinsicSet = new Set(intrinsics);

            const controlKeywords = [
                'if', 'else', 'for', 'while', 'do', 'switch', 'case', 'default',
                'break', 'continue', 'return', 'discard',
            ];
            const modifierKeywords = [
                'static', 'const', 'uniform', 'in', 'out', 'inout', 'inline',
                'extern', 'shared', 'groupshared', 'globallycoherent', 'volatile',
                'precise', 'nointerpolation', 'noperspective', 'centroid', 'linear',
                'row_major', 'column_major', 'snorm', 'unorm', 'unsigned',
                'export', 'indices', 'vertices', 'primitives', 'payload',
            ];
            const typeKeywords = [
                'void', 'bool', 'int', 'uint', 'dword', 'half', 'float', 'double',
                'string', 'vector', 'matrix',
                'bool1','bool2','bool3','bool4',
                'int1','int2','int3','int4',
                'uint1','uint2','uint3','uint4',
                'half1','half2','half3','half4',
                'float1','float2','float3','float4',
                'double1','double2','double3','double4',
                'bool1x1','bool1x2','bool1x3','bool1x4',
                'bool2x1','bool2x2','bool2x3','bool2x4',
                'bool3x1','bool3x2','bool3x3','bool3x4',
                'bool4x1','bool4x2','bool4x3','bool4x4',
                'int1x1','int1x2','int1x3','int1x4',
                'int2x1','int2x2','int2x3','int2x4',
                'int3x1','int3x2','int3x3','int3x4',
                'int4x1','int4x2','int4x3','int4x4',
                'uint1x1','uint1x2','uint1x3','uint1x4',
                'uint2x1','uint2x2','uint2x3','uint2x4',
                'uint3x1','uint3x2','uint3x3','uint3x4',
                'uint4x1','uint4x2','uint4x3','uint4x4',
                'half1x1','half1x2','half1x3','half1x4',
                'half2x1','half2x2','half2x3','half2x4',
                'half3x1','half3x2','half3x3','half3x4',
                'half4x1','half4x2','half4x3','half4x4',
                'float1x1','float1x2','float1x3','float1x4',
                'float2x1','float2x2','float2x3','float2x4',
                'float3x1','float3x2','float3x3','float3x4',
                'float4x1','float4x2','float4x3','float4x4',
                'double1x1','double1x2','double1x3','double1x4',
                'double2x1','double2x2','double2x3','double2x4',
                'double3x1','double3x2','double3x3','double3x4',
                'double4x1','double4x2','double4x3','double4x4',
                'min16float','min16float1','min16float2','min16float3','min16float4',
                'min16int','min16int1','min16int2','min16int3','min16int4',
                'min16uint','min16uint1','min16uint2','min16uint3','min16uint4',
                'min12int','min12int1','min12int2','min12int3','min12int4',
                'min10float','min10float1','min10float2','min10float3','min10float4',
            ];
            const objectKeywords = [
                'struct', 'class', 'interface', 'typedef', 'namespace',
                'cbuffer', 'tbuffer', 'technique', 'technique10', 'technique11', 'pass',
                'SamplerState', 'SamplerComparisonState',
                'sampler', 'sampler1D', 'sampler2D', 'sampler3D', 'samplerCUBE',
                'Texture', 'Texture2DLegacy', 'TextureCubeLegacy',
                'Texture1D', 'Texture1DArray',
                'Texture2D', 'Texture2DArray', 'Texture2DMS', 'Texture2DMSArray',
                'Texture3D', 'TextureCube', 'TextureCubeArray',
                'RWTexture1D', 'RWTexture1DArray',
                'RWTexture2D', 'RWTexture2DArray',
                'RWTexture3D',
                'Buffer', 'ByteAddressBuffer', 'StructuredBuffer',
                'RWBuffer', 'RWByteAddressBuffer', 'RWStructuredBuffer',
                'AppendStructuredBuffer', 'ConsumeStructuredBuffer',
                'RasterizerOrderedBuffer', 'RasterizerOrderedByteAddressBuffer',
                'RasterizerOrderedStructuredBuffer',
                'RasterizerOrderedTexture1D', 'RasterizerOrderedTexture1DArray',
                'RasterizerOrderedTexture2D', 'RasterizerOrderedTexture2DArray',
                'RasterizerOrderedTexture3D',
                'InputPatch', 'OutputPatch',
                'LineStream', 'TriangleStream', 'PointStream',
                'BlendState', 'DepthStencilState', 'RasterizerState',
            ];
            const literalKeywords = ['true', 'false', 'NULL'];

            const allKeywords = [
                ...controlKeywords,
                ...modifierKeywords,
                ...typeKeywords,
                ...objectKeywords,
                ...literalKeywords,
            ];
            const keywordSet = new Set(allKeywords);

            monaco.languages.registerCompletionItemProvider('hlsl', {
                provideCompletionItems: function (model, position) {
                    const word = model.getWordUntilPosition(position);
                    const range = {
                        startLineNumber: position.lineNumber,
                        endLineNumber: position.lineNumber,
                        startColumn: word.startColumn,
                        endColumn: word.endColumn,
                    };

                    // Collect unique words from the document as identifier suggestions
                    const content = model.getValue();
                    const wordRegex = /[a-zA-Z_]\w*/g;
                    const seen = new Set([...intrinsicSet, ...keywordSet]);
                    const wordSuggestions = [];
                    let m;
                    while ((m = wordRegex.exec(content)) !== null) {
                        if (!seen.has(m[0])) {
                            seen.add(m[0]);
                            wordSuggestions.push({
                                label: m[0],
                                kind: monaco.languages.CompletionItemKind.Text,
                                insertText: m[0],
                                range: range,
                            });
                        }
                    }

                    const intrinsicSuggestions = intrinsics.map(name => ({
                        label: name,
                        kind: monaco.languages.CompletionItemKind.Function,
                        insertText: noArgIntrinsics.has(name) ? name + '()' : name + '($0)',
                        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                        range: range,
                    }));

                    const keywordSuggestions = allKeywords.map(name => ({
                        label: name,
                        kind: monaco.languages.CompletionItemKind.Keyword,
                        insertText: name,
                        range: range,
                    }));

                    return { suggestions: [...keywordSuggestions, ...intrinsicSuggestions, ...wordSuggestions] };
                },
            });

            // Debug hover: show variable value under cursor
            monaco.languages.registerHoverProvider('hlsl', {
                provideHover: async function (model, position) {
                    if (!window._dotNetDebugRef) return null;
                    var word = model.getWordAtPosition(position);
                    if (!word) return null;
                    var result = await window._dotNetDebugRef.invokeMethodAsync('GetHoverValue', word.word);
                    if (result == null) return null;
                    return {
                        range: new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn),
                        contents: [{ value: '```\n' + word.word + ' = ' + result + '\n```' }]
                    };
                }
            });
        })();

        monaco.editor.defineTheme('hlsl-dark', {
            base: 'vs-dark',
            inherit: true,
            rules: [
                { token: 'keyword.control',   foreground: 'c586c0' },   // purple  - if/else/for/return/discard
                { token: 'keyword.modifier',  foreground: '569cd6' },   // blue    - static/const/inout/...
                { token: 'keyword.type',      foreground: '4ec9b0' },   // teal    - float/int/Texture2D/...
                { token: 'keyword.literal',   foreground: '569cd6' },   // blue    - true/false
                { token: 'keyword.directive', foreground: '9b9b9b' },   // grey    - #define/#include
                { token: 'support.function',  foreground: 'dcdcaa' },   // yellow  - intrinsics
                { token: 'annotation',        foreground: 'c8c8c8' },
                { token: 'annotation.bracket',foreground: 'c8c8c8' },
                { token: 'number',            foreground: 'b5cea8' },
                { token: 'number.float',      foreground: 'b5cea8' },
                { token: 'number.hex',        foreground: 'b5cea8' },
                { token: 'string',            foreground: 'ce9178' },
                { token: 'comment',           foreground: '6a9955' },
                { token: 'identifier',        foreground: '9cdcfe' },
                { token: 'operator',          foreground: 'd4d4d4' },
            ],
            colors: {}
        });

        window._monacoEditor = monaco.editor.create(document.getElementById(containerId), {
            value: initialCode,
            language: 'hlsl',
            theme: 'hlsl-dark',
            fontFamily: "'JetBrains Mono', 'Cascadia Code', 'Consolas', 'Courier New', monospace",
            fontSize: 16,
            lineNumbers: 'on',
            lineNumbersMinChars: 3,
            glyphMargin: true,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            automaticLayout: true,
            wordWrap: 'off',
            tabSize: 4,
            insertSpaces: true,
        });

        window._monacoEditor.addCommand(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter,
            function () { document.querySelector('.btn-run').click(); }
        );

        // Drag-and-drop a file onto the editor to open it
        var editorDom = document.getElementById(containerId);
        editorDom.addEventListener('dragover', function (e) { e.preventDefault(); });
        editorDom.addEventListener('drop', function (e) {
            e.preventDefault();
            var file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
            if (!file) return;
            if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessageWithAdditionalObjects === 'function') {
                // On desktop (WebView2), send the File to the host so it can read the path and content in C#
                window.chrome.webview.postMessageWithAdditionalObjects('FileDrop', [file]);
            } else {
                // On the web, read via FileReader since the filesystem path isn't available
                var reader = new FileReader();
                reader.onload = function (ev) {
                    if (window._dotNetDebugRef)
                        window._dotNetDebugRef.invokeMethodAsync('OpenFileInTab', file.name, ev.target.result, '');
                    else
                        window._monacoEditor.setValue(ev.target.result);
                };
                reader.readAsText(file);
            }
        });

        // Gutter click to toggle a breakpoint
        window._monacoEditor.onMouseDown(function (e) {
            var t = e.target.type;
            if ((t === monaco.editor.MouseTargetType.GUTTER_LINE_NUMBERS ||
                 t === monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN) &&
                e.target.position && window._dotNetDebugRef) {
                window._dotNetDebugRef.invokeMethodAsync('ToggleBreakpoint', e.target.position.lineNumber);
            }
        });

        // Debug hotkeys when editor has focus
        function clickBtn(title) {
            var btn = document.querySelector('[title="' + title + '"]');
            if (btn && !btn.disabled) btn.click();
        }
        function clickDbg(action) {
            var btn = document.querySelector('[data-dbg="' + action + '"]');
            if (btn && !btn.disabled) btn.click();
        }
        function toggleBreakpointAtCursor() {
            if (window._dotNetDebugRef) {
                var pos = window._monacoEditor.getPosition();
                if (pos) window._dotNetDebugRef.invokeMethodAsync('ToggleBreakpoint', pos.lineNumber);
            }
        }
        window._monacoEditor.addCommand(monaco.KeyCode.F5, function () {
            var cont = document.querySelector('[data-dbg="continue"]');
            var start = document.querySelector('.btn-debug-start');
            if (cont && !cont.disabled) cont.click();
            else if (start && !start.disabled) start.click();
        });
        window._monacoEditor.addCommand(monaco.KeyMod.Shift | monaco.KeyCode.F5, function () { clickDbg('continue-back'); });
        window._monacoEditor.addCommand(monaco.KeyCode.F9, function () { toggleBreakpointAtCursor(); });
        window._monacoEditor.addCommand(monaco.KeyCode.F10, function () { clickDbg('step-over'); });
        window._monacoEditor.addCommand(monaco.KeyMod.Shift | monaco.KeyCode.F10, function () { clickDbg('step-over-back'); });
        window._monacoEditor.addCommand(monaco.KeyCode.F11, function () { clickDbg('step-in'); });
        window._monacoEditor.addCommand(monaco.KeyMod.Shift | monaco.KeyCode.F11, function () { clickDbg('step-in-back'); });
        window._monacoEditor.addCommand(monaco.KeyCode.F12, function () { clickDbg('step-out'); });
        window._monacoEditor.addCommand(monaco.KeyMod.Shift | monaco.KeyCode.F12, function () { clickDbg('step-out-back'); });

        window.monacoEditorInitialized = true;
    });
};

// Global debug hotkeys (when Monaco does not have focus)
document.addEventListener('keydown', function (e) {
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
    var mc = document.getElementById('monaco-container');
    if (mc && mc.contains(document.activeElement)) return;

    function clickDbgGlobal(action) {
        var btn = document.querySelector('[data-dbg="' + action + '"]');
        if (btn && !btn.disabled) btn.click();
    }
    if (e.key === 'F5' && !e.shiftKey) {
        e.preventDefault();
        var cont = document.querySelector('[data-dbg="continue"]');
        var start = document.querySelector('.btn-debug-start');
        if (cont && !cont.disabled) cont.click();
        else if (start && !start.disabled) start.click();
    } else if (e.key === 'F5' && e.shiftKey) {
        e.preventDefault();
        clickDbgGlobal('continue-back');
    } else if (e.key === 'F9') {
        e.preventDefault();
        if (window._monacoEditor && window._dotNetDebugRef) {
            var pos = window._monacoEditor.getPosition();
            if (pos) window._dotNetDebugRef.invokeMethodAsync('ToggleBreakpoint', pos.lineNumber);
        }
    } else if (e.key === 'F10' && !e.shiftKey) {
        e.preventDefault();
        clickDbgGlobal('step-over');
    } else if (e.key === 'F10' && e.shiftKey) {
        e.preventDefault();
        clickDbgGlobal('step-over-back');
    } else if (e.key === 'F11' && !e.shiftKey) {
        e.preventDefault();
        clickDbgGlobal('step-in');
    } else if (e.key === 'F11' && e.shiftKey) {
        e.preventDefault();
        clickDbgGlobal('step-in-back');
    } else if (e.key === 'F12' && !e.shiftKey) {
        e.preventDefault();
        clickDbgGlobal('step-out');
    } else if (e.key === 'F12' && e.shiftKey) {
        e.preventDefault();
        clickDbgGlobal('step-out-back');
    }
});

// Update breakpoint decorations in Monaco gutter
window.setBreakpoints = function (lines) {
    if (!window._monacoEditor) return;
    var decorations = (lines || []).map(function (line) {
        return {
            range: new monaco.Range(line, 1, line, 1),
            options: {
                glyphMarginClassName: 'dbg-breakpoint-glyph',
                glyphMarginHoverMessage: { value: 'Breakpoint' },
                overviewRuler: { color: '#e51400', position: monaco.editor.OverviewRulerLane.Left },
            }
        };
    });
    window._breakpointDecorationIds = window._monacoEditor.deltaDecorations(
        window._breakpointDecorationIds || [], decorations
    );
};

// Highlight the current debug line (0 to clear)
window.highlightDebugLine = function (lineNumber) {
    if (!window._monacoEditor) return;
    var decorations = lineNumber > 0 ? [{
        range: new monaco.Range(lineNumber, 1, lineNumber, 1),
        options: {
            isWholeLine: true,
            className: 'dbg-current-line',
            glyphMarginClassName: 'dbg-current-glyph',
            overviewRuler: { color: '#ffcc00', position: monaco.editor.OverviewRulerLane.Center },
        }
    }] : [];
    window._debugLineDecorationIds = window._monacoEditor.deltaDecorations(
        window._debugLineDecorationIds || [], decorations
    );
    if (lineNumber > 0) window._monacoEditor.revealLineInCenter(lineNumber);
};

window.renderPixels = function (canvasId, rgbaData, width, height) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    const imageData = new ImageData(new Uint8ClampedArray(rgbaData), width, height);
    ctx.putImageData(imageData, 0, 0);
    window.fitCanvas(canvas, width, height);
};

window.fitCanvas = function (canvas, width, height) {
    const container = canvas.parentElement;
    if (!container) return;
    const cw = container.clientWidth;
    const ch = container.clientHeight;
    const ar = width / height;
    let cssW, cssH;
    if (cw / ch > ar) {
        cssH = ch;
        cssW = ch * ar;
    } else {
        cssW = cw;
        cssH = cw / ar;
    }
    canvas.style.width = Math.floor(cssW) + 'px';
    canvas.style.height = Math.floor(cssH) + 'px';
};

window.restoreImageSectionHeight = function () {
    if (window._imageSectionHeight) {
        const el = document.querySelector('.image-section, .debug-section-image');
        if (el) el.style.setProperty('--section-h', window._imageSectionHeight);
    }
};

window.copyToClipboard = function (text) {
    return navigator.clipboard.writeText(text);
};

window.scrollImmediateToBottom = function () {
    requestAnimationFrame(function () {
        var el = document.querySelector('.imm-body');
        if (el) el.scrollTop = el.scrollHeight;
    });
};

window.saveSectionHeights = function () {
    window._savedSectionHeights = {};
    document.querySelectorAll('[class*="debug-section-"]').forEach(function (el) {
        var key = Array.from(el.classList).find(function (c) { return c.startsWith('debug-section-'); });
        if (!key) return;
        var h = el.style.getPropertyValue('--section-h');
        if (h) window._savedSectionHeights[key] = h;
    });
};

window.restoreSectionHeights = function () {
    if (!window._savedSectionHeights) return;
    Object.entries(window._savedSectionHeights).forEach(function ([cls, h]) {
        var el = document.querySelector('.' + cls);
        if (el) el.style.setProperty('--section-h', h);
    });
};

window._threadGridCols = 1;
window._threadGridRows = 1;
window._threadGridResizeObserver = null;

window.fitThreadGrid = function (containerId, cols, rows) {
    const container = document.getElementById(containerId);
    if (!container) return;
    const grid = container.querySelector('.thread-grid');
    if (!grid) return;
    const cw = container.clientWidth - 12;  // account for padding
    const ch = container.clientHeight - 12;
    if (cw <= 0 || ch <= 0 || cols <= 0 || rows <= 0) return;
    // Account for gaps in the cell size calculation to prevent overflow/clipping.
    // Try gap=2 first; fall back to gap=1 for small cells.
    let gap = 2;
    let cellSize = Math.max(2, Math.floor(Math.min(
        (cw - (cols - 1) * gap) / cols,
        (ch - (rows - 1) * gap) / rows
    )));
    if (cellSize <= 4) {
        gap = 1;
        cellSize = Math.max(2, Math.floor(Math.min(
            (cw - (cols - 1) * gap) / cols,
            (ch - (rows - 1) * gap) / rows
        )));
    }
    grid.style.gridTemplateColumns = `repeat(${cols}, ${cellSize}px)`;
    grid.style.gridTemplateRows = `repeat(${rows}, ${cellSize}px)`;
    grid.style.gap = gap + 'px';
};

window.initThreadGridResize = function (containerId, cols, rows) {
    window.disposeThreadGridResize();
    window._threadGridCols = cols;
    window._threadGridRows = rows;
    const container = document.getElementById(containerId);
    if (!container) return;
    window.fitThreadGrid(containerId, cols, rows);
    window._threadGridResizeObserver = new ResizeObserver(function () {
        window.fitThreadGrid(containerId, cols, rows);
    });
    window._threadGridResizeObserver.observe(container);
};

window.disposeThreadGridResize = function () {
    if (window._threadGridResizeObserver) {
        window._threadGridResizeObserver.disconnect();
        window._threadGridResizeObserver = null;
    }
};

// Inner horizontal resize: call stack / execution state divider
(function () {
    var dragging = false;
    var startX, startW, pane;

    document.addEventListener('mousedown', function (e) {
        if (!e.target.classList.contains('resize-inner-h-handle')) return;
        dragging = true;
        startX = e.clientX;
        var body = e.target.closest('.callstack-exec-body');
        pane = body ? body.querySelector('.callstack-pane') : null;
        if (!pane) { dragging = false; return; }
        startW = pane.getBoundingClientRect().width;
        e.target.classList.add('dragging');
        e.preventDefault();
    });

    document.addEventListener('mousemove', function (e) {
        if (!dragging || !pane) return;
        var body = pane.closest('.callstack-exec-body');
        var totalW = body ? body.getBoundingClientRect().width : 0;
        var newW = Math.max(40, Math.min(totalW - 44, startW + (e.clientX - startX)));
        pane.style.flex = '0 0 ' + newW + 'px';
    });

    document.addEventListener('mouseup', function (e) {
        if (!dragging) return;
        dragging = false;
        var handle = document.getElementById('callstack-exec-divider');
        if (handle) handle.classList.remove('dragging');
        pane = null;
    });
})();

window.getMonacoValue = function () {
    if (window._monacoEditor) {
        return window._monacoEditor.getValue();
    }
    return '';
};

window.setMonacoValue = function (value) {
    if (window._monacoEditor) {
        window._monacoEditor.setValue(value);
    }
};

window.setMonacoFontSize = function (size) {
    if (window._monacoEditor) {
        window._monacoEditor.updateOptions({ fontSize: size });
    }
};

// Outer resize: horizontal panel + vertical sections
(function () {
    let activeHandle = null;  // null | 'horizontal' | { type: 'vertical', section: Element }
    let startPos, startSize;

    document.addEventListener('mousedown', function (e) {
        if (e.target.classList.contains('resize-h-handle')) {
            activeHandle = 'horizontal';
            startPos = e.clientX;
            startSize = document.querySelector('.output-panel').getBoundingClientRect().width;
            e.preventDefault();
        } else if (e.target.classList.contains('resize-v-handle')) {
            const section = e.target.closest('.image-section, .debug-section-image, .debug-section-console, .debug-section-vars, .debug-section-callstack, .debug-section-immediate');
            if (section) {
                activeHandle = { type: 'vertical', section };
                startPos = e.clientY;
                startSize = section.getBoundingClientRect().height;
                e.preventDefault();
            }
        }
    });

    document.addEventListener('mousemove', function (e) {
        if (!activeHandle) return;
        if (activeHandle === 'horizontal') {
            const newWidth = Math.max(160, startSize - (e.clientX - startPos));
            document.querySelector('.output-panel').style.width = newWidth + 'px';
            const canvas = document.getElementById('color-canvas');
            if (canvas && canvas.width && canvas.height)
                window.fitCanvas(canvas, canvas.width, canvas.height);
            // Thread grid reacts via ResizeObserver automatically
        } else if (activeHandle && activeHandle.type === 'vertical') {
            // Handle is at the bottom of the section: drag down = grow
            const newHeight = Math.max(60, Math.min(800, startSize + (e.clientY - startPos)));
            // Use CSS custom property so .section-collapsed { height: auto } can still override
            activeHandle.section.style.setProperty('--section-h', newHeight + 'px');
            if (activeHandle.section.classList.contains('image-section') ||
                activeHandle.section.classList.contains('debug-section-image')) {
                const canvas = document.getElementById('color-canvas');
                if (canvas && canvas.width && canvas.height)
                    window.fitCanvas(canvas, canvas.width, canvas.height);
            }
            // exec-state / thread-grid: ResizeObserver on exec-state-body handles it
        }
        e.preventDefault();
    });

    document.addEventListener('mouseup', function () {
        if (activeHandle && activeHandle.type === 'vertical') {
            if (activeHandle.section.classList.contains('image-section') ||
                activeHandle.section.classList.contains('debug-section-image')) {
                window._imageSectionHeight = activeHandle.section.style.getPropertyValue('--section-h');
            }
        }
        activeHandle = null;
    });
})();
