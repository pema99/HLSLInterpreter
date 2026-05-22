import { rgbaToDataUrl, getDebuggerRef } from './host.js';

let _monacoEditor = null;
let _models = new Map();
let _currentModelId = -1;

export function dbgIsTabDropAfter(tabIndex, clientX) {
    const tabs = document.querySelectorAll('.editor-tab');
    const el = tabs[tabIndex];
    if (!el) return false;
    const r = el.getBoundingClientRect();
    return clientX > r.left + r.width / 2;
};

export function initMonaco(containerId, docId, initialCode) {
    require.config({
        paths: { 'vs': 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.0/min/vs' }
    });
    return new Promise(function (resolve) {
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

            monaco.languages.registerHoverProvider('hlsl', {
                provideHover: async function (model, position) {
                    var ref = getDebuggerRef();
                    if (!ref) return null;
                    var word = model.getWordAtPosition(position);
                    if (!word) return null;
                    var info = await ref.invokeMethodAsync('GetHoverInfo', word.word);
                    if (info == null) return null;
                    var contents = [{ value: '```\n' + word.word + ' = ' + info.value + '\n```' }];
                    if (info.perThreadValues) {
                        var rgba = info.rgba;
                        var lines = info.perThreadValues.map(function (v, i) {
                            var swatch = '   ';
                            if (rgba && rgba.length >= (i + 1) * 4) {
                                var r = rgba[i*4], g = rgba[i*4+1], b = rgba[i*4+2];
                                var svg = '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
                                    + '<rect width="10" height="10" fill="rgb(' + r + ',' + g + ',' + b + ')" stroke="#555"/></svg>';
                                var url = 'data:image/svg+xml;utf8,' + encodeURIComponent(svg);
                                swatch = '<img src="' + url + '" width="10" height="10" />';
                            }
                            return swatch + ' [' + i + '] ' + v;
                        }).join('<br/>');
                        contents.push({
                            value: lines,
                            supportHtml: true,
                            isTrusted: true,
                        });
                    } else if (info.rgba && info.width > 0 && info.height > 0) {
                        var url = rgbaToDataUrl(info.rgba, info.width, info.height, info.inspectedX, info.inspectedY);
                        contents.push({
                            value: `<img src="${url}" />`,
                            supportHtml: true,
                            isTrusted: true,
                        });
                    }
                    return {
                        range: new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn),
                        contents: contents,
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

        monaco.editor.defineTheme('hlsl-bonzomatic', {
            base: 'vs-dark',
            inherit: true,
            rules: [
                { token: 'keyword.control',   foreground: 'c586c0' },
                { token: 'keyword.modifier',  foreground: '569cd6' },
                { token: 'keyword.type',      foreground: '4ec9b0' },
                { token: 'keyword.literal',   foreground: '569cd6' },
                { token: 'keyword.directive', foreground: '9b9b9b' },
                { token: 'support.function',  foreground: 'dcdcaa' },
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
            colors: {
                'editor.background': '#00000000',
                'editorGutter.background': '#00000000',
                'editor.lineHighlightBackground': '#ffffff14',
                'editor.lineHighlightBorder': '#00000000',
                'minimap.background': '#00000000',
                'scrollbarSlider.background': '#80808060',
                'editorOverviewRuler.background': '#00000000',
            }
        });

        _monacoEditor = monaco.editor.create(document.getElementById(containerId), {
            language: 'hlsl',
            theme: 'hlsl-dark',
            fontFamily: "'JetBrains Mono', 'Cascadia Code', 'Consolas', 'Courier New', monospace",
            fontLigatures: false,
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
            stickyScroll: { enabled: false },
        });

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
                    var ref = getDebuggerRef();
                    if (ref)
                        ref.invokeMethodAsync('OnFileDropped', file.name, ev.target.result);
                    else
                        _monacoEditor.setValue(ev.target.result);
                };
                reader.readAsText(file);
            }
        });

        // Gutter click to toggle a breakpoint
        _monacoEditor.onMouseDown(function (e) {
            var t = e.target.type;
            var ref = getDebuggerRef();
            if ((t === monaco.editor.MouseTargetType.GUTTER_LINE_NUMBERS ||
                 t === monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN) &&
                e.target.position && ref) {
                ref.invokeMethodAsync('ToggleBreakpoint', e.target.position.lineNumber);
            }
        });

        document.fonts.ready.then(function () {
            monaco.editor.remeasureFonts();
        });

        createModel(docId, initialCode);
        showModel(docId);
        resolve();
    });
    });
};

// Keyboard shortcuts
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        var overlays = document.querySelectorAll('.modal-overlay');
        if (overlays.length > 0) {
            overlays[overlays.length - 1].click();
            e.preventDefault();
        }
        return;
    }
    // Ignore form fields, but not Monaco's own textarea (we want F-keys there).
    var t = e.target;
    if (t.tagName === 'INPUT' || t.tagName === 'SELECT') return;
    if (t.tagName === 'TEXTAREA' && !t.closest('.monaco-editor')) return;

    function click(selector) {
        var el = document.querySelector(selector);
        if (el && !el.disabled) { el.click(); return true; }
        return false;
    }
    function dbg(action) { return click('[data-dbg="' + action + '"]'); }

    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        click('.btn-run');
        return;
    }
    switch (e.key) {
        case 'F5':
            e.preventDefault();
            if (e.shiftKey) dbg('continue-back');
            else if (!dbg('continue')) click('.btn-run');
            break;
        case 'F9':
            e.preventDefault();
            var bpRef = getDebuggerRef();
            if (_monacoEditor && bpRef) {
                var pos = _monacoEditor.getPosition();
                if (pos) bpRef.invokeMethodAsync('ToggleBreakpoint', pos.lineNumber);
            }
            break;
        case 'F10':
            e.preventDefault();
            if (e.shiftKey) dbg('step-over-back');
            else if (!dbg('step-over')) {
                var shell = document.querySelector('.app-shell.bonzomatic');
                if (shell) shell.classList.toggle('editor-hidden');
            }
            break;
        case 'F11':
            e.preventDefault();
            dbg(e.shiftKey ? 'step-in-back' : 'step-in');
            break;
        case 'F12':
            e.preventDefault();
            dbg(e.shiftKey ? 'step-out-back' : 'step-out');
            break;
    }
});

export function setBreakpoints(docId, lines) {
    var e = _models.get(docId);
    if (!e) return;
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
    e.bpIds = e.model.deltaDecorations(e.bpIds, decorations);
};

// Highlight the current debug line (0 to clear)
export function highlightDebugLine(lineNumber) {
    _models.forEach(function (e) {
        if (e.lineIds.length) e.lineIds = e.model.deltaDecorations(e.lineIds, []);
    });
    var cur = _models.get(_currentModelId);
    if (cur && lineNumber > 0) {
        cur.lineIds = cur.model.deltaDecorations([], [{
            range: new monaco.Range(lineNumber, 1, lineNumber, 1),
            options: {
                isWholeLine: true,
                className: 'dbg-current-line',
                glyphMarginClassName: 'dbg-current-glyph',
                overviewRuler: { color: '#ffcc00', position: monaco.editor.OverviewRulerLane.Center },
            }
        }]);
        if (_monacoEditor) _monacoEditor.revealLineInCenter(lineNumber);
    }
};

export function getMonacoValue() {
    return _monacoEditor ? _monacoEditor.getValue() : '';
};

// One Monaco model per document/tab
export function createModel(id, content) {
    var existing = _models.get(id);
    if (existing) { existing.model.setValue(content); return; }
    var model = monaco.editor.createModel(content, 'hlsl');
    _models.set(id, { model: model, bpIds: [], lineIds: [] });
};

export function showModel(id) {
    var e = _models.get(id);
    if (e && _monacoEditor) { _monacoEditor.setModel(e.model); _currentModelId = id; }
};

export function setModelContent(id, content) {
    var e = _models.get(id);
    if (e) e.model.setValue(content);
    else createModel(id, content);
};

export function disposeModel(id) {
    var e = _models.get(id);
    if (e) { e.model.dispose(); _models.delete(id); }
};

export function setMonacoTheme(theme) {
    if (_monacoEditor) monaco.editor.setTheme(theme);
};

export function setMonacoFontSize(size) {
    if (_monacoEditor) {
        _monacoEditor.updateOptions({ fontSize: size });
    }
};

export function setMonacoReadOnly(readOnly) {
    if (_monacoEditor) {
        _monacoEditor.updateOptions({
            readOnly: readOnly,
            readOnlyMessage: { value: 'Cannot edit code while debugging. Stop the debug session first.' }
        });
    }
};

