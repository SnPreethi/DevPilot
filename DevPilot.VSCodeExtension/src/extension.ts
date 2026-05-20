import * as vscode from 'vscode';
import { ChatViewProvider, DevPilotDiffProvider } from './chatViewProvider';
import { DevPilotInlineCompletionProvider } from './inlineCompletionProvider';
import { DevPilotCodeActionsProvider } from './codeActionsProvider';
import { GraphViewProvider } from './graphViewProvider';

export function activate(context: vscode.ExtensionContext) {
    const diffProvider = new DevPilotDiffProvider();
    
    // Register text document content provider for virtual diff views
    context.subscriptions.push(
        vscode.workspace.registerTextDocumentContentProvider('devpilot-diff', diffProvider)
    );

    const provider = new ChatViewProvider(context, diffProvider);
    const graphProvider = new GraphViewProvider(context);

    // Register Webview View Provider for Sidebar Graph
    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider(GraphViewProvider.viewType, graphProvider)
    );

    // Register Inline Completion Provider
    const inlineCompletionProvider = new DevPilotInlineCompletionProvider();
    context.subscriptions.push(
        vscode.languages.registerInlineCompletionItemProvider(
            { pattern: '**/*' },
            inlineCompletionProvider
        )
    );

    // Register Code Actions Provider for Quick Fixes
    context.subscriptions.push(
        vscode.languages.registerCodeActionsProvider(
            { pattern: '**/*' },
            new DevPilotCodeActionsProvider(provider),
            {
                providedCodeActionKinds: DevPilotCodeActionsProvider.providedCodeActionKinds
            }
        )
    );

    // Register devpilot.fixDiagnostic command
    context.subscriptions.push(
        vscode.commands.registerCommand('devpilot.fixDiagnostic', async (diagnostic: vscode.Diagnostic, document: vscode.TextDocument) => {
            const line = diagnostic.range.start.line;
            const totalLines = document.lineCount;
            const startLine = Math.max(0, line - 10);
            const endLine = Math.min(totalLines - 1, line + 10);
            
            const surroundingCode = document.getText(new vscode.Range(
                new vscode.Position(startLine, 0),
                new vscode.Position(endLine, document.lineAt(endLine).text.length)
            ));

            await provider.fixDiagnostic(diagnostic, surroundingCode, document.fileName);
        })
    );

    // Register devpilot.analyzeTerminalSelection command
    context.subscriptions.push(
        vscode.commands.registerCommand('devpilot.analyzeTerminalSelection', async () => {
            const activeTerminal = vscode.window.activeTerminal;
            if (!activeTerminal) {
                vscode.window.showInformationMessage('No active terminal found.');
                return;
            }

            // Copy selection from the terminal to the clipboard
            await vscode.commands.executeCommand('workbench.action.terminal.copySelection');
            const selectedText = await vscode.env.clipboard.readText();

            if (!selectedText || selectedText.trim().length === 0) {
                vscode.window.showWarningMessage('Please select the failing logs or compiler errors in the terminal before running this command.');
                return;
            }

            await provider.analyzeTerminalSelection(selectedText);
        })
    );

    // Register Webview View Provider for Sidebar
    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider('devpilot.chatView', provider)
    );

    // Register Explain Selection Command
    context.subscriptions.push(
        vscode.commands.registerCommand('devpilot.explainSelection', async () => {
            const editor = vscode.window.activeTextEditor;
            if (!editor) {
                vscode.window.showInformationMessage('No active editor found.');
                return;
            }

            const selection = editor.selection;
            const selectionText = editor.document.getText(selection);

            if (!selectionText || selectionText.trim().length === 0) {
                vscode.window.showWarningMessage('Please select some code to explain.');
                return;
            }

            const filePath = editor.document.uri.fsPath;
            const languageId = editor.document.languageId;

            await provider.explainSelection(selectionText, filePath, languageId);
        })
    );

    // Helper command execution logic
    const executeEditAction = async (promptLabel: string, commandName: string) => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showInformationMessage('No active editor found.');
            return;
        }

        const selection = editor.selection;
        const selectionText = editor.document.getText(selection);
        if (!selectionText || selectionText.trim().length === 0) {
            vscode.window.showWarningMessage(`Please select some code to trigger ${commandName}.`);
            return;
        }

        const filePath = editor.document.uri.fsPath;
        const languageId = editor.document.languageId;

        await provider.triggerEditWorkflow(promptLabel, selectionText, filePath, languageId);
    };

    // Register Refactoring / Editing Commands
    context.subscriptions.push(
        vscode.commands.registerCommand('devpilot.refactorSelection', () => 
            executeEditAction('Refactor the selected code.', 'Refactor')
        ),
        vscode.commands.registerCommand('devpilot.optimizeSelection', () => 
            executeEditAction('Optimize the selected code for performance and readability.', 'Optimize')
        ),
        vscode.commands.registerCommand('devpilot.generateTests', () => 
            executeEditAction('Generate comprehensive unit tests for the selected code.', 'Generate Tests')
        ),
        vscode.commands.registerCommand('devpilot.addLogging', () => 
            executeEditAction('Add standard logging to the selected code.', 'Add Logging')
        ),
        vscode.commands.registerCommand('devpilot.convertToAsync', () => 
            executeEditAction('Convert the selected code to use async/await.', 'Convert to Async')
        )
    );

    // Register Clear Chat Command
    context.subscriptions.push(
        vscode.commands.registerCommand('devpilot.clearChat', () => {
            vscode.window.showInformationMessage('Clear chat action triggered.');
        })
    );

    // Register Knowledge Graph commands
    const triggerGraphCommand = async (commandName: string) => {
        const editor = vscode.window.activeTextEditor;
        let nodeId = 'workspace-root';
        if (editor) {
            const document = editor.document;
            const selection = editor.selection;
            const selectedText = document.getText(selection).trim();
            if (selectedText) {
                // If there's selected text, use it as symbol/node identifier
                nodeId = selectedText;
            } else {
                // Otherwise use the active file path as node identifier
                nodeId = document.fileName;
            }
        }
        vscode.window.showInformationMessage(`Knowledge Graph: Loading ${commandName} for "${nodeId}"...`);
        await graphProvider.showNodeInGraph(nodeId);
    };

    context.subscriptions.push(
        vscode.commands.registerCommand('devpilot.showRelationships', () => triggerGraphCommand('Relationships')),
        vscode.commands.registerCommand('devpilot.showDependencyGraph', () => triggerGraphCommand('Dependency Graph')),
        vscode.commands.registerCommand('devpilot.showLineage', () => triggerGraphCommand('Lineage'))
    );
}

export function deactivate() {}
