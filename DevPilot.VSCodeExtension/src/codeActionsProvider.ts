import * as vscode from 'vscode';
import { ChatViewProvider } from './chatViewProvider';

export class DevPilotCodeActionsProvider implements vscode.CodeActionProvider {
    public static readonly providedCodeActionKinds = [
        vscode.CodeActionKind.QuickFix
    ];

    constructor(_chatProvider: ChatViewProvider) {}

    public provideCodeActions(
        document: vscode.TextDocument,
        _range: vscode.Range | vscode.Selection,
        context: vscode.CodeActionContext,
        _token: vscode.CancellationToken
    ): vscode.CodeAction[] {
        // Target errors and warnings
        const diagnostics = context.diagnostics.filter(
            d => d.severity === vscode.DiagnosticSeverity.Error || d.severity === vscode.DiagnosticSeverity.Warning
        );

        if (diagnostics.length === 0) {
            return [];
        }

        return diagnostics.map(diagnostic => {
            const shortMessage = diagnostic.message.length > 50 
                ? diagnostic.message.substring(0, 50) + '...' 
                : diagnostic.message;
            const action = new vscode.CodeAction(`Fix with DevPilot: ${shortMessage}`, vscode.CodeActionKind.QuickFix);
            action.command = {
                title: 'Fix with DevPilot',
                command: 'devpilot.fixDiagnostic',
                arguments: [diagnostic, document]
            };
            action.diagnostics = [diagnostic];
            action.isPreferred = true;
            return action;
        });
    }
}
