import * as vscode from 'vscode';
import * as crypto from 'crypto';

export class DevPilotInlineCompletionProvider implements vscode.InlineCompletionItemProvider {
    
    public async provideInlineCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position,
        _context: vscode.InlineCompletionContext,
        token: vscode.CancellationToken
    ): Promise<vscode.InlineCompletionList | vscode.InlineCompletionItem[] | undefined> {
        
        // 1. Keystroke debounce delay (150ms) to prevent excessive backend requests
        try {
            await new Promise<void>((resolve, reject) => {
                const timer = setTimeout(resolve, 150);
                token.onCancellationRequested(() => {
                    clearTimeout(timer);
                    reject(new vscode.CancellationError());
                });
            });
        } catch {
            return undefined;
        }

        if (token.isCancellationRequested) {
            return undefined;
        }

        // 2. Prepare FIM contexts
        const prefix = document.getText(new vscode.Range(new vscode.Position(0, 0), position));
        const lastLine = document.lineCount - 1;
        const lastLineLength = document.lineAt(lastLine).text.length;
        const suffix = document.getText(new vscode.Range(position, new vscode.Position(lastLine, lastLineLength)));

        // 3. Resolve workspace details
        const workspaceFolder = vscode.workspace.getWorkspaceFolder(document.uri);
        const repositoryPath = workspaceFolder ? workspaceFolder.uri.fsPath : undefined;
        const repositoryId = repositoryPath ? crypto.createHash('sha256').update(repositoryPath).digest('hex') : undefined;

        // 4. Extract local document imports and nearby declarations
        const text = document.getText();
        const imports: string[] = [];
        const importRegex = /^(import\s+.*from|using\s+[a-zA-Z0-9_\.]+;)/gm;
        let match;
        while ((match = importRegex.exec(text)) !== null) {
            imports.push(match[0].trim());
            if (imports.length >= 10) break;
        }

        const nearby: string[] = [];
        const minLine = Math.max(0, position.line - 50);
        const maxLine = Math.min(document.lineCount - 1, position.line + 50);
        for (let l = minLine; l <= maxLine; l++) {
            const lineText = document.lineAt(l).text.trim();
            if ((lineText.startsWith('public') || lineText.startsWith('private') || lineText.startsWith('function') || lineText.startsWith('const ') || lineText.startsWith('let ')) &&
                (lineText.includes('(') || lineText.includes('{'))) {
                nearby.push(lineText);
                if (nearby.length >= 5) break;
            }
        }

        const currentLine = document.lineAt(position.line).text;
        const currentLinePrefix = currentLine.substring(0, position.character);

        // 5. Query LocalService /completion
        const abortController = new AbortController();
        token.onCancellationRequested(() => {
            abortController.abort();
        });

        try {
            const response = await fetch('http://localhost:5071/completion', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    filePath: document.uri.fsPath,
                    languageId: document.languageId,
                    cursorLine: position.line + 1,
                    cursorColumn: position.character + 1,
                    prefixContent: prefix,
                    suffixContent: suffix,
                    repositoryId,
                    repositoryPath,
                    imports,
                    nearbySymbols: nearby
                }),
                signal: abortController.signal
            });

            if (!response.ok || !response.body) {
                return undefined;
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder('utf-8');
            let buffer = '';
            let completedCode = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                buffer += decoder.decode(value, { stream: true });
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';

                for (const line of lines) {
                    const cleanLine = line.trim();
                    if (cleanLine.startsWith('data: ')) {
                        const dataStr = cleanLine.substring(6).trim();
                        try {
                            const parsed = JSON.parse(dataStr);
                            if (parsed.type === 'content' && parsed.text) {
                                completedCode += parsed.text;
                            } else if (parsed.type === 'done') {
                                break;
                            }
                        } catch {
                            // ignore malformed JSON chunk
                        }
                    }
                }
            }

            // Flush remaining buffer
            if (buffer.trim().startsWith('data: ')) {
                const dataStr = buffer.trim().substring(6).trim();
                try {
                    const parsed = JSON.parse(dataStr);
                    if (parsed.type === 'content' && parsed.text) {
                        completedCode += parsed.text;
                    }
                } catch {}
            }

            // 6. Completion Filtering
            const cleanCompletion = this.filterCompletion(completedCode, currentLinePrefix);
            if (!cleanCompletion || cleanCompletion.trim().length === 0) {
                return undefined;
            }

            const inlineItem = new vscode.InlineCompletionItem(
                cleanCompletion,
                new vscode.Range(position, position)
            );

            return [inlineItem];

        } catch (err) {
            // Suppress cancellations and fetch network errors silently
            return undefined;
        }
    }

    private filterCompletion(text: string, currentLinePrefix: string): string {
        let clean = text;

        // Remove markdown formatting block markers
        clean = clean.replace(/^```[a-zA-Z]*\r?\n/, '');
        clean = clean.replace(/```$/, '');

        // Remove system tags
        clean = clean.replace(/<\|end\|>/g, '');
        clean = clean.replace(/<\|user\|>/g, '');
        clean = clean.replace(/<\|system\|>/g, '');
        clean = clean.replace(/<\|assistant\|>/g, '');

        // If completion echoes the prefix of the current line, slice it off
        if (clean.startsWith(currentLinePrefix)) {
            clean = clean.substring(currentLinePrefix.length);
        }

        // Avoid leading newlines followed by excessive blank spaces
        if (clean.startsWith('\n') && currentLinePrefix.trim().length === 0) {
            // Keep clean
        }

        return clean;
    }
}
