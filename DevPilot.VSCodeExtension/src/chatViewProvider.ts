import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import * as crypto from 'crypto';

export class DevPilotDiffProvider implements vscode.TextDocumentContentProvider {
    private _onDidChange = new vscode.EventEmitter<vscode.Uri>();
    readonly onDidChange = this._onDidChange.event;
    private _contents = new Map<string, string>();

    public update(uri: vscode.Uri, content: string) {
        this._contents.set(uri.toString(), content);
        this._onDidChange.fire(uri);
    }

    public provideTextDocumentContent(uri: vscode.Uri): string {
        return this._contents.get(uri.toString()) || '';
    }
}

export class ChatViewProvider implements vscode.WebviewViewProvider {
    private _view?: vscode.WebviewView;
    private _abortController?: AbortController;
    private _statusInterval?: NodeJS.Timeout;

    constructor(
        private readonly context: vscode.ExtensionContext,
        private readonly diffProvider: DevPilotDiffProvider
    ) {}

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        _context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this.context.extensionUri]
        };

        webviewView.webview.html = this.getHtmlContent();

        // Listen for messages from the webview
        webviewView.webview.onDidReceiveMessage(async (message) => {
            switch (message.type) {
                case 'sendMessage':
                    await this.handleUserMessage(message.prompt);
                    break;
                case 'applyEditPlan':
                    await this.applyEditPlan(message.plan);
                    break;
                case 'revertEditPlan':
                    await this.revertEditPlan();
                    break;
                case 'openDiff':
                    await this.openDiff(message.filePath, message.patchedContent);
                    break;
                case 'refreshWorkflows':
                    await this.pollWorkflows();
                    break;
                case 'startWorkflow':
                    await this.startWorkflow(message.objective);
                    break;
                case 'advanceWorkflow':
                    await this.advanceWorkflow(message.workflowId, message.taskId, message.targetStatus, message.approvalGranted);
                    break;
                case 'pauseWorkflow':
                    await this.updateWorkflowStatus('pause', message.workflowId);
                    break;
                case 'resumeWorkflow':
                    await this.updateWorkflowStatus('resume', message.workflowId);
                    break;
                case 'cancelWorkflow':
                    await this.updateWorkflowStatus('cancel', message.workflowId);
                    break;
                case 'refreshExecutions':
                    await this.pollExecutions();
                    break;
                case 'startExecution':
                    await this.startExecution(message.workflowId, message.workflowTaskId, message.objective, message.dryRun);
                    break;
                case 'validateExecution':
                    await this.validateExecution(message.pipelineId);
                    break;
                case 'approveExecution':
                    await this.approveExecution(message.pipelineId);
                    break;
                case 'cancelExecution':
                    await this.cancelExecution(message.pipelineId);
                    break;
                case 'rollbackExecution':
                    await this.rollbackExecution(message.pipelineId);
                    break;
            }
        });

        // Set up polling for runtime status (every 3 seconds)
        this.startStatusPolling();

        webviewView.onDidDispose(() => {
            this.stopStatusPolling();
        });
    }

    private getHtmlContent(): string {
        const htmlPath = path.join(this.context.extensionPath, 'src', 'webview', 'chat.html');
        return fs.readFileSync(htmlPath, 'utf8');
    }

    private startStatusPolling() {
        this.stopStatusPolling();
        this.pollStatus();
        this._statusInterval = setInterval(() => this.pollStatus(), 3000);
    }

    private stopStatusPolling() {
        if (this._statusInterval) {
            clearInterval(this._statusInterval);
            this._statusInterval = undefined;
        }
    }

    private async pollStatus() {
        if (!this._view) return;
        try {
            const response = await fetch('http://localhost:5071/runtime-status');
            if (response.ok) {
                const status = await response.json();
                this._view.webview.postMessage({ type: 'statusUpdate', status });
                await this.pollWorkflows();
                await this.pollExecutions();
            } else {
                this._view.webview.postMessage({ type: 'statusUpdate', status: null });
            }
        } catch {
            this._view.webview.postMessage({ type: 'statusUpdate', status: null });
        }
    }

    private getRepositoryId(): string | undefined {
        if (vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0) {
            const rootPath = vscode.workspace.workspaceFolders[0].uri.fsPath;
            const normalizedPath = rootPath.toUpperCase();
            return crypto.createHash('sha256').update(normalizedPath).digest('hex').toLowerCase();
        }
        return undefined;
    }

    private async pollWorkflows() {
        if (!this._view) return;

        try {
            const repoId = this.getRepositoryId();
            const query = repoId ? `?repositoryId=${encodeURIComponent(repoId)}` : '';
            const response = await fetch(`http://localhost:5071/workflow/list${query}`);
            if (!response.ok) {
                this._view.webview.postMessage({ type: 'workflowUpdate', workflows: [] });
                return;
            }

            const workflows = await response.json();
            const active = Array.isArray(workflows) && workflows.length > 0
                ? await this.fetchWorkflow(workflows[0].workflowId || workflows[0].WorkflowId)
                : null;

            this._view.webview.postMessage({ type: 'workflowUpdate', workflows, active });
        } catch {
            this._view.webview.postMessage({ type: 'workflowUpdate', workflows: [] });
        }
    }

    private async pollExecutions() {
        if (!this._view) return;

        try {
            const response = await fetch('http://localhost:5071/execution/pipelines');
            if (!response.ok) {
                this._view.webview.postMessage({ type: 'executionUpdate', pipelines: [] });
                return;
            }

            const pipelines = await response.json();
            const active = Array.isArray(pipelines) && pipelines.length > 0
                ? await this.fetchExecutionPipeline(pipelines[0].pipelineId || pipelines[0].PipelineId)
                : null;

            this._view.webview.postMessage({ type: 'executionUpdate', pipelines, active });
        } catch {
            this._view.webview.postMessage({ type: 'executionUpdate', pipelines: [] });
        }
    }

    private async fetchExecutionPipeline(pipelineId: string) {
        if (!pipelineId) return null;
        const response = await fetch(`http://localhost:5071/execution/pipeline/${encodeURIComponent(pipelineId)}`);
        return response.ok ? await response.json() : null;
    }

    private async fetchWorkflow(workflowId: string) {
        if (!workflowId) return null;
        const response = await fetch(`http://localhost:5071/workflow/${encodeURIComponent(workflowId)}`);
        return response.ok ? await response.json() : null;
    }

    private async startWorkflow(objective: string) {
        if (!this._view) return;

        const repoId = this.getRepositoryId();
        const repoPath = this.getRepositoryPath();
        if (!objective || objective.trim().length === 0) {
            this._view.webview.postMessage({ type: 'workflowError', message: 'Workflow objective is required.' });
            return;
        }

        try {
            const response = await fetch('http://localhost:5071/workflow/start', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    objective,
                    repositoryId: repoId,
                    repositoryPath: repoPath
                })
            });

            if (!response.ok) {
                const text = await response.text();
                this._view.webview.postMessage({ type: 'workflowError', message: text || 'Failed to start workflow.' });
                return;
            }

            const active = await response.json();
            this._view.webview.postMessage({ type: 'workflowStarted', active });
            await this.pollWorkflows();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'workflowError', message: error.message || 'Workflow start failed.' });
        }
    }

    private async advanceWorkflow(workflowId: string, taskId: string, targetStatus: string, approvalGranted: boolean) {
        if (!this._view) return;

        try {
            const response = await fetch('http://localhost:5071/workflow/advance', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    workflowId,
                    taskId,
                    targetStatus,
                    approvalGranted: approvalGranted === true
                })
            });

            if (!response.ok) {
                const text = await response.text();
                this._view.webview.postMessage({ type: 'workflowError', message: text || 'Failed to advance workflow.' });
                return;
            }

            await this.pollWorkflows();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'workflowError', message: error.message || 'Workflow advance failed.' });
        }
    }

    private async updateWorkflowStatus(action: 'pause' | 'resume' | 'cancel', workflowId: string) {
        if (!this._view) return;

        try {
            const response = await fetch(`http://localhost:5071/workflow/${action}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ workflowId })
            });

            if (!response.ok) {
                const text = await response.text();
                this._view.webview.postMessage({ type: 'workflowError', message: text || `Failed to ${action} workflow.` });
                return;
            }

            await this.pollWorkflows();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'workflowError', message: error.message || `Workflow ${action} failed.` });
        }
    }

    private async startExecution(workflowId: string, workflowTaskId: string | undefined, objective: string, dryRun: boolean) {
        if (!this._view) return;

        const repoId = this.getRepositoryId();
        const repoPath = this.getRepositoryPath();
        if (!workflowId || !objective) {
            this._view.webview.postMessage({ type: 'executionError', message: 'Workflow and objective are required to start execution.' });
            return;
        }

        try {
            const response = await fetch('http://localhost:5071/execution/start', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    workflowId,
                    workflowTaskId,
                    objective,
                    repositoryId: repoId,
                    repositoryPath: repoPath,
                    dryRun: dryRun !== false,
                    validationOnly: false
                })
            });

            if (!response.ok) {
                this._view.webview.postMessage({ type: 'executionError', message: await response.text() });
                return;
            }

            await this.pollExecutions();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'executionError', message: error.message || 'Execution start failed.' });
        }
    }

    private async validateExecution(pipelineId: string) {
        if (!this._view) return;
        try {
            const response = await fetch('http://localhost:5071/execution/validate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    pipelineId,
                    isValid: true,
                    messages: ['Validation checkpoint accepted from VS Code dashboard.']
                })
            });
            if (!response.ok) {
                this._view.webview.postMessage({ type: 'executionError', message: await response.text() });
                return;
            }
            await this.pollExecutions();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'executionError', message: error.message || 'Execution validation failed.' });
        }
    }

    private async approveExecution(pipelineId: string) {
        if (!this._view) return;
        try {
            const response = await fetch('http://localhost:5071/execution/approve', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pipelineId, approvedBy: 'vscode-user' })
            });
            if (!response.ok) {
                this._view.webview.postMessage({ type: 'executionError', message: await response.text() });
                return;
            }
            await this.pollExecutions();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'executionError', message: error.message || 'Execution approval failed.' });
        }
    }

    private async cancelExecution(pipelineId: string) {
        if (!this._view) return;
        try {
            const response = await fetch('http://localhost:5071/execution/cancel', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pipelineId, reason: 'Cancelled from VS Code dashboard.' })
            });
            if (!response.ok) {
                this._view.webview.postMessage({ type: 'executionError', message: await response.text() });
                return;
            }
            await this.pollExecutions();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'executionError', message: error.message || 'Execution cancel failed.' });
        }
    }

    private async rollbackExecution(pipelineId: string) {
        if (!this._view) return;
        const repoPath = this.getRepositoryPath();
        if (!repoPath) {
            this._view.webview.postMessage({ type: 'executionError', message: 'Repository path is required for rollback.' });
            return;
        }

        try {
            const response = await fetch('http://localhost:5071/execution/rollback', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pipelineId, repositoryPath: repoPath, reason: 'Rollback requested from VS Code dashboard.' })
            });
            if (!response.ok) {
                this._view.webview.postMessage({ type: 'executionError', message: await response.text() });
                return;
            }
            await this.pollExecutions();
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'executionError', message: error.message || 'Execution rollback failed.' });
        }
    }

    private getRepositoryPath(): string | undefined {
        if (vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0) {
            return vscode.workspace.workspaceFolders[0].uri.fsPath;
        }
        return undefined;
    }

    private getActiveEditorContext() {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            return {};
        }

        const document = editor.document;
        const selection = editor.selection;

        const activeFilePath = document.uri.fsPath;
        const cursorLine = selection.active.line + 1; // 1-indexed for backend
        
        let selectedCode: string | undefined = undefined;
        if (!selection.isEmpty) {
            selectedCode = document.getText(selection);
        }

        // Get surrounding lines (e.g. 50 lines before and after cursor)
        const totalLines = document.lineCount;
        const startLine = Math.max(0, cursorLine - 51);
        const endLine = Math.min(totalLines - 1, cursorLine + 49);
        const surroundingLines = document.getText(new vscode.Range(
            new vscode.Position(startLine, 0),
            new vscode.Position(endLine, document.lineAt(endLine).text.length)
        ));

        const languageId = document.languageId;

        // Parse visible imports (simple regex on the first 100 lines)
        const visibleImports: string[] = [];
        const scanLimit = Math.min(100, totalLines);
        const importRegex = /import\s+(?:[^"']*?\s+from\s+)?["']([^"']+)["']/g;
        const usingRegex = /using\s+([^;]+);/g;

        for (let i = 0; i < scanLimit; i++) {
            const lineText = document.lineAt(i).text;
            let match;
            while ((match = importRegex.exec(lineText)) !== null) {
                visibleImports.push(match[1]);
            }
            while ((match = usingRegex.exec(lineText)) !== null) {
                visibleImports.push(match[1].trim());
            }
        }

        return {
            activeFilePath,
            cursorLine,
            selectedCode,
            surroundingLines,
            languageId,
            visibleImports
        };
    }

    private async handleUserMessage(prompt: string) {
        if (!this._view) return;

        // Abort previous generation if active
        if (this._abortController) {
            this._abortController.abort();
        }
        this._abortController = new AbortController();

        try {
            const repoId = this.getRepositoryId();
            const editorContext = this.getActiveEditorContext();

            const response = await fetch('http://localhost:5071/chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    prompt: prompt,
                    repositoryId: repoId,
                    activeFilePath: editorContext.activeFilePath,
                    cursorLine: editorContext.cursorLine,
                    selectedCode: editorContext.selectedCode,
                    surroundingLines: editorContext.surroundingLines,
                    languageId: editorContext.languageId,
                    visibleImports: editorContext.visibleImports
                }),
                signal: this._abortController.signal
            });

            if (!response.ok) {
                const errText = await response.text();
                this._view.webview.postMessage({ type: 'streamError', message: errText || 'API Server Error' });
                return;
            }

            await this.streamResponse(response);

        } catch (error: any) {
            if (error.name !== 'AbortError') {
                this._view.webview.postMessage({ type: 'streamError', message: error.message || 'Connection Error' });
            }
        }
    }

    public async explainSelection(codeSnippet: string, filePath: string, languageId: string) {
        if (!this._view) {
            vscode.window.showErrorMessage('DevPilot view is not visible. Please open the sidebar first.');
            return;
        }

        // Focus the sidebar view
        this._view.show(true);

        // Notify webview that explain selection has started
        this._view.webview.postMessage({ type: 'explainTriggered' });

        if (this._abortController) {
            this._abortController.abort();
        }
        this._abortController = new AbortController();

        try {
            const repoId = this.getRepositoryId();
            const editorContext = this.getActiveEditorContext();

            const response = await fetch('http://localhost:5071/explain-selection', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    codeSnippet,
                    filePath,
                    languageId,
                    repositoryId: repoId,
                    cursorLine: editorContext.cursorLine,
                    surroundingLines: editorContext.surroundingLines,
                    visibleImports: editorContext.visibleImports
                }),
                signal: this._abortController.signal
            });

            if (!response.ok) {
                const errText = await response.text();
                this._view.webview.postMessage({ type: 'streamError', message: errText || 'API Server Error' });
                return;
            }

            await this.streamResponse(response);

        } catch (error: any) {
            if (error.name !== 'AbortError') {
                this._view.webview.postMessage({ type: 'streamError', message: error.message || 'Connection Error' });
            }
        }
    }

    public async triggerEditWorkflow(promptLabel: string, _selectionText: string, _filePath: string, _languageId: string) {
        if (!this._view) {
            vscode.window.showErrorMessage('DevPilot view is not visible. Please open the sidebar first.');
            return;
        }

        this._view.show(true);
        this._view.webview.postMessage({ type: 'editPlanStart', prompt: promptLabel });

        try {
            const repoId = this.getRepositoryId();
            const repoPath = this.getRepositoryPath();
            const editorContext = this.getActiveEditorContext();

            if (!repoId || !repoPath) {
                this._view.webview.postMessage({ 
                    type: 'editPlanError', 
                    message: 'Workspace folder is required to run refactoring/editing workflows.' 
                });
                return;
            }

            const response = await fetch('http://localhost:5071/edit/plan', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    prompt: promptLabel,
                    repositoryId: repoId,
                    repositoryPath: repoPath,
                    activeFilePath: editorContext.activeFilePath,
                    cursorLine: editorContext.cursorLine,
                    selectedCode: editorContext.selectedCode,
                    surroundingLines: editorContext.surroundingLines,
                    languageId: editorContext.languageId,
                    visibleImports: editorContext.visibleImports
                })
            });

            if (!response.ok) {
                const errText = await response.text();
                let errMsg = 'Failed to generate edit plan.';
                try {
                    const parsedErr = JSON.parse(errText);
                    if (parsedErr.error) errMsg = parsedErr.error;
                } catch {}
                this._view.webview.postMessage({ type: 'editPlanError', message: errMsg });
                return;
            }

            const data = await response.json();
            const plan = data.plan;
            const preview = data.preview;

            this._view.webview.postMessage({ 
                type: 'editPlanPreview', 
                plan, 
                preview 
            });

            // Automatically open diff preview for the first file edit
            if (preview.filePreviews && preview.filePreviews.length > 0) {
                const primaryEdit = preview.filePreviews[0];
                if (primaryEdit.isValid) {
                    await this.openDiff(primaryEdit.filePath, primaryEdit.patchedContent);
                }
            }

        } catch (error: any) {
            this._view.webview.postMessage({ 
                type: 'editPlanError', 
                message: error.message || 'Connection to DevPilot backend failed.' 
            });
        }
    }

    public async fixDiagnostic(diagnostic: any, surroundingCode: string, filePath: string) {
        if (!this._view) {
            vscode.window.showErrorMessage('DevPilot view is not visible. Please open the sidebar first.');
            return;
        }

        this._view.show(true);
        this._view.webview.postMessage({ type: 'editPlanStart', prompt: `Fixing: ${diagnostic.message}` });

        try {
            const repoId = this.getRepositoryId();
            const repoPath = this.getRepositoryPath();

            if (!repoId || !repoPath) {
                this._view.webview.postMessage({ 
                    type: 'editPlanError', 
                    message: 'Workspace folder is required to run diagnostics fixes.' 
                });
                return;
            }

            const response = await fetch('http://localhost:5071/diagnostics/fix', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    filePath,
                    diagnostic: {
                        filePath,
                        line: diagnostic.range.start.line + 1,
                        column: diagnostic.range.start.character + 1,
                        severity: diagnostic.severity === vscode.DiagnosticSeverity.Error ? 0 : 1,
                        message: diagnostic.message,
                        code: String(diagnostic.code || 'ERROR'),
                        source: diagnostic.source || 'VSCode'
                    },
                    surroundingCode,
                    repositoryId: repoId,
                    repositoryPath: repoPath
                })
            });

            if (!response.ok) {
                const errText = await response.text();
                let errMsg = 'Failed to generate fix plan.';
                try {
                    const parsedErr = JSON.parse(errText);
                    if (parsedErr.error) errMsg = parsedErr.error;
                } catch {}
                this._view.webview.postMessage({ type: 'editPlanError', message: errMsg });
                return;
            }

            const data = await response.json();
            const plan = data.plan;
            const preview = data.preview;

            this._view.webview.postMessage({ 
                type: 'editPlanPreview', 
                plan, 
                preview 
            });

            if (preview.filePreviews && preview.filePreviews.length > 0) {
                const primaryEdit = preview.filePreviews[0];
                if (primaryEdit.isValid) {
                    await this.openDiff(primaryEdit.filePath, primaryEdit.patchedContent);
                }
            }

        } catch (error: any) {
            this._view.webview.postMessage({ 
                type: 'editPlanError', 
                message: error.message || 'Connection to DevPilot backend failed.' 
            });
        }
    }

    public async analyzeTerminalSelection(selectedText: string) {
        if (!this._view) {
            vscode.window.showErrorMessage('DevPilot view is not visible. Please open the sidebar first.');
            return;
        }

        this._view.show(true);
        this._view.webview.postMessage({ type: 'editPlanStart', prompt: 'Analyzing terminal execution failure...' });

        try {
            const repoId = this.getRepositoryId();
            const repoPath = this.getRepositoryPath();

            if (!repoPath) {
                this._view.webview.postMessage({ 
                    type: 'editPlanError', 
                    message: 'Workspace folder is required to analyze terminal output.' 
                });
                return;
            }

            const response = await fetch('http://localhost:5071/execution/analyze', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    event: {
                        type: 0,
                        message: '',
                        rawOutput: selectedText
                    },
                    repositoryId: repoId,
                    repositoryPath: repoPath
                })
            });

            if (!response.ok) {
                const errText = await response.text();
                let errMsg = 'Failed to analyze execution failure.';
                try {
                    const parsedErr = JSON.parse(errText);
                    if (parsedErr.error) errMsg = parsedErr.error;
                } catch {}
                this._view.webview.postMessage({ type: 'editPlanError', message: errMsg });
                return;
            }

            const data = await response.json();
            const plan = data.plan;
            const preview = data.preview;

            this._view.webview.postMessage({ 
                type: 'editPlanPreview', 
                plan, 
                preview 
            });

            if (preview.filePreviews && preview.filePreviews.length > 0) {
                const primaryEdit = preview.filePreviews[0];
                if (primaryEdit.isValid) {
                    await this.openDiff(primaryEdit.filePath, primaryEdit.patchedContent);
                }
            }

        } catch (error: any) {
            this._view.webview.postMessage({ 
                type: 'editPlanError', 
                message: error.message || 'Connection to DevPilot backend failed.' 
            });
        }
    }

    private async openDiff(relativeFilePath: string, patchedContent: string) {
        const repoPath = this.getRepositoryPath();
        if (!repoPath) return;

        const fullPath = path.join(repoPath, relativeFilePath);
        const originalUri = vscode.Uri.file(fullPath);
        
        // Create virtual URI for the patched file
        const virtualUri = vscode.Uri.parse(`devpilot-diff://preview/${relativeFilePath.replace(/\\/g, '/')}`);
        
        this.diffProvider.update(virtualUri, patchedContent);

        const fileName = path.basename(fullPath);
        await vscode.commands.executeCommand(
            'vscode.diff',
            originalUri,
            virtualUri,
            `${fileName} (Original) ↔ ${fileName} (DevPilot Proposed Edit)`
        );
    }

    private async applyEditPlan(plan: any) {
        if (!this._view) return;
        
        try {
            const repoPath = this.getRepositoryPath();
            if (!repoPath) return;

            const response = await fetch('http://localhost:5071/edit/apply', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    plan,
                    repositoryPath: repoPath
                })
            });

            if (!response.ok) {
                const errText = await response.text();
                let errMsg = 'Failed to apply changes.';
                try {
                    const parsedErr = JSON.parse(errText);
                    if (parsedErr.error) errMsg = parsedErr.error;
                } catch {}
                this._view.webview.postMessage({ type: 'editPlanApplyResult', success: false, message: errMsg });
                return;
            }

            this._view.webview.postMessage({ type: 'editPlanApplyResult', success: true });
            vscode.window.showInformationMessage('DevPilot: Proposed changes successfully applied!');
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'editPlanApplyResult', success: false, message: error.message });
        }
    }

    private async revertEditPlan() {
        if (!this._view) return;

        try {
            const repoPath = this.getRepositoryPath();
            if (!repoPath) return;

            const response = await fetch('http://localhost:5071/edit/revert', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    repositoryPath: repoPath
                })
            });

            if (!response.ok) {
                const errText = await response.text();
                let errMsg = 'Failed to revert changes.';
                try {
                    const parsedErr = JSON.parse(errText);
                    if (parsedErr.error) errMsg = parsedErr.error;
                } catch {}
                this._view.webview.postMessage({ type: 'editPlanRevertResult', success: false, message: errMsg });
                return;
            }

            this._view.webview.postMessage({ type: 'editPlanRevertResult', success: true });
            vscode.window.showInformationMessage('DevPilot: Reverted changes successfully.');
        } catch (error: any) {
            this._view.webview.postMessage({ type: 'editPlanRevertResult', success: false, message: error.message });
        }
    }

    private async streamResponse(response: Response) {
        if (!this._view || !response.body) return;

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let hasStarted = false;

        while (true) {
            const { value, done } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop() || '';

            for (const line of lines) {
                const trimmed = line.trim();
                if (trimmed.startsWith('data: ')) {
                    const dataStr = trimmed.slice(6);
                    try {
                        const parsed = JSON.parse(dataStr);
                        if (parsed.type === 'context') {
                            this._view.webview.postMessage({ type: 'streamContext', context: parsed.data });
                        } else if (parsed.type === 'content') {
                            if (!hasStarted) {
                                this._view.webview.postMessage({ type: 'streamStart' });
                                hasStarted = true;
                            }
                            this._view.webview.postMessage({ type: 'streamChunk', text: parsed.text });
                        } else if (parsed.type === 'done') {
                            this._view.webview.postMessage({ type: 'streamDone' });
                        } else if (parsed.type === 'cancelled') {
                            this._view.webview.postMessage({ type: 'streamDone' });
                        } else if (parsed.type === 'error') {
                            this._view.webview.postMessage({ type: 'streamError', message: parsed.message });
                        }
                    } catch {
                        // Ignore syntax errors on partial lines
                    }
                }
            }
        }
    }
}
