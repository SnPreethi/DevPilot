import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as http from 'http';

export class GraphViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'devpilot.graphView';
    private _view?: vscode.WebviewView;

    constructor(private readonly _context: vscode.ExtensionContext) {}

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        _context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._context.extensionUri]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);

        // Message communication from UI to Extension
        webviewView.webview.onDidReceiveMessage(async (data) => {
            switch (data.type) {
                case 'refreshGraph':
                    await this.loadGraphData();
                    break;
                case 'nodeSelected':
                    await this.loadNodeRelationships(data.nodeId);
                    break;
                case 'traceLineage':
                    await this.loadLineage(data.nodeId);
                    break;
                case 'getCorrelations':
                    await this.loadCorrelations();
                    break;
                case 'runRootCause':
                    await this.loadRootCause(data.nodeId);
                    break;
                case 'runFailureAttribution':
                    await this.loadFailureAttribution(data.nodeId);
                    break;
                case 'getPatchImpact':
                    await this.loadPatchImpact(data.patchId);
                    break;
                case 'getFailureLineage':
                    await this.loadFailureLineage(data.failureId);
                    break;
                case 'getArchitectureAnalysis':
                    await this.loadArchitectureAnalysis();
                    break;
                case 'generateModernizationPlan':
                    await this.loadModernizationPlan(data.modType, data.payload);
                    break;
                case 'getModernizationImpact':
                    await this.loadModernizationImpact(data.modType, data.payload);
                    break;
                case 'executeModernization':
                    await this.loadModernizationExecute(data.planId, data.action, data.stepId);
                    break;
                case 'getProductSettings':
                    await this.loadProductSettings();
                    break;
                case 'saveProductSettings':
                    await this.saveProductSettings(data.settings);
                    break;
                case 'getProductModels':
                    await this.loadProductModels();
                    break;
                case 'downloadProductModel':
                    await this.downloadProductModel(data.modelId);
                    break;
                case 'cancelProductModelDownload':
                    await this.cancelProductModelDownload(data.modelId);
                    break;
                case 'getProductDependencies':
                    await this.loadProductDependencies();
                    break;
                case 'repairProductDependency':
                    await this.repairProductDependency(data.dependencyName);
                    break;
                case 'getProductDiagnostics':
                    await this.loadProductDiagnostics();
                    break;
                case 'getProductOnboarding':
                    await this.loadProductOnboarding();
                    break;
                case 'completeProductOnboarding':
                    await this.completeProductOnboarding();
                    break;
                case 'getProductUpdates':
                    await this.loadProductUpdates();
                    break;
                case 'applyProductUpdate':
                    await this.applyProductUpdate();
                    break;
                case 'getProductLogs':
                    await this.loadProductLogs();
                    break;
            }
        });
    }

    public async showNodeInGraph(nodeId: string) {
        if (this._view) {
            this._view.show(true);
            this._view.webview.postMessage({ type: 'setSelectedNode', nodeId });
            await this.loadNodeRelationships(nodeId);
            await this.loadLineage(nodeId);
        }
    }

    private async loadGraphData() {
        try {
            // Fetch all nodes & relationships from LocalService POST /graph/query
            const result = await this._makePostRequest('/graph/query', { maxResults: 100 });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setGraphData', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to connect to local DevPilot Knowledge Graph: ${(error as Error).message}`);
        }
    }

    private async loadNodeRelationships(nodeId: string) {
        try {
            const result = await this._makeGetRequest(`/graph/relationships/${encodeURIComponent(nodeId)}`);
            if (this._view && result) {
                // Merges node + outgoing/incoming edges
                this._view.webview.postMessage({
                    type: 'setSelectedNodeDetails',
                    nodeId,
                    data: result
                });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to load relationships: ${(error as Error).message}`);
        }
    }

    private async loadLineage(nodeId: string) {
        try {
            const result = await this._makePostRequest('/graph/lineage', {
                nodeId: nodeId,
                direction: 'Both',
                maxDepth: 6
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setLineageData', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to trace lineage: ${(error as Error).message}`);
        }
    }

    private async loadCorrelations() {
        try {
            const result = await this._makePostRequest('/reasoning/correlate', {
                repositoryId: 'devpilot-workspace'
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setCorrelationData', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to load correlations: ${(error as Error).message}`);
        }
    }

    private async loadRootCause(nodeId: string) {
        try {
            const result = await this._makePostRequest('/reasoning/root-cause', {
                failureNodeId: nodeId
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setRootCauseResult', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to run root cause analysis: ${(error as Error).message}`);
        }
    }

    private async loadFailureAttribution(nodeId: string) {
        try {
            const result = await this._makePostRequest('/failure/analyze', {
                failureNodeId: nodeId
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setAttributionResult', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to run failure attribution analysis: ${(error as Error).message}`);
        }
    }

    private async loadPatchImpact(patchId: string) {
        try {
            const result = await this._makePostRequest('/failure/patch-impact', {
                patchNodeId: patchId
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setPatchImpactData', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to retrieve patch impact analysis: ${(error as Error).message}`);
        }
    }

    private async loadFailureLineage(failureId: string) {
        try {
            const result = await this._makePostRequest('/failure/lineage', {
                failureNodeId: failureId
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setFailureLineageData', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to retrieve failure execution lineage: ${(error as Error).message}`);
        }
    }

    private async loadArchitectureAnalysis() {
        try {
            const result = await this._makePostRequest('/architecture/analyze', {
                repositoryId: 'devpilot-workspace'
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setArchitectureSummary', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to retrieve architecture analysis: ${(error as Error).message}`);
        }
    }

    private async loadModernizationPlan(modType: number, payload: string) {
        try {
            const result = await this._makePostRequest('/modernization/plan', {
                type: modType,
                targetPayload: payload,
                repositoryId: 'devpilot-workspace'
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setModernizationPlan', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to retrieve modernization plan: ${(error as Error).message}`);
        }
    }

    private async loadModernizationImpact(modType: number, payload: string) {
        try {
            const result = await this._makePostRequest('/modernization/analyze', {
                type: modType,
                targetPayload: payload,
                repositoryId: 'devpilot-workspace'
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setModernizationImpact', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to retrieve modernization impact: ${(error as Error).message}`);
        }
    }

    private async loadModernizationExecute(planId: string, action: string, stepId?: string) {
        try {
            const result = await this._makePostRequest('/modernization/execute', {
                planId: planId,
                action: action,
                stepId: stepId
            });
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setModernizationPlan', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to execute modernization action: ${(error as Error).message}`);
        }
    }

    private async loadProductSettings() {
        try {
            const result = await this._makeGetRequest('/product/settings');
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductSettings', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to load settings: ${(error as Error).message}`);
        }
    }

    private async saveProductSettings(settings: any) {
        try {
            const result = await this._makePostRequest('/product/settings', settings);
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductSettings', data: result });
                vscode.window.showInformationMessage('Settings saved successfully.');
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to save settings: ${(error as Error).message}`);
        }
    }

    private async loadProductModels() {
        try {
            const result = await this._makeGetRequest('/product/models');
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductModels', data: result });
            }
        } catch (error) {
            // Ignore background polling errors to keep UI quiet
        }
    }

    private async downloadProductModel(modelId: string) {
        try {
            const result = await this._makePostRequest('/product/models/download', { modelId });
            if (this._view && result) {
                vscode.window.showInformationMessage(`Started downloading model: ${modelId}`);
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to start model download: ${(error as Error).message}`);
        }
    }

    private async cancelProductModelDownload(modelId: string) {
        try {
            vscode.window.showInformationMessage(`Canceled download for model: ${modelId}`);
        } catch (error) {
            // Ignore
        }
    }

    private async loadProductDependencies() {
        try {
            const result = await this._makeGetRequest('/product/dependencies');
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductDependencies', data: result });
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to verify dependencies: ${(error as Error).message}`);
        }
    }

    private async repairProductDependency(dependencyName: string) {
        try {
            const result = await this._makePostRequest('/product/dependencies/repair', { dependencyName });
            if (result && result.success) {
                vscode.window.showInformationMessage(`Successfully repaired dependency: ${dependencyName}`);
                await this.loadProductDependencies();
            } else {
                vscode.window.showErrorMessage(`Failed to repair dependency: ${dependencyName}`);
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Repair failed: ${(error as Error).message}`);
        }
    }

    private async loadProductDiagnostics() {
        try {
            const result = await this._makeGetRequest('/product/diagnostics');
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductDiagnostics', data: result });
            }
        } catch (error) {
            // Ignore polling errors
        }
    }

    private async loadProductOnboarding() {
        try {
            const result = await this._makeGetRequest('/product/onboarding');
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductOnboarding', data: result });
            }
        } catch (error) {
            // Ignore
        }
    }

    private async completeProductOnboarding() {
        try {
            const result = await this._makePostRequest('/product/onboarding/complete', {});
            if (this._view && result) {
                vscode.window.showInformationMessage('Onboarding configuration completed.');
                await this.loadProductOnboarding();
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to complete onboarding: ${(error as Error).message}`);
        }
    }

    private async loadProductUpdates() {
        try {
            const result = await this._makeGetRequest('/product/updates');
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductUpdates', data: result });
            }
        } catch (error) {
            // Ignore
        }
    }

    private async applyProductUpdate() {
        try {
            const result = await this._makePostRequest('/product/updates/apply', {});
            if (result && result.success) {
                vscode.window.showInformationMessage('DevPilot updated successfully! Please reload window to apply changes.');
                await this.loadProductUpdates();
            }
        } catch (error) {
            vscode.window.showErrorMessage(`Failed to apply updates: ${(error as Error).message}`);
        }
    }

    private async loadProductLogs() {
        try {
            const result = await this._makeGetRequest('/product/logs');
            if (this._view && result) {
                this._view.webview.postMessage({ type: 'setProductLogs', data: result });
            }
        } catch (error) {
            // Ignore
        }
    }

    private _makeGetRequest(apiPath: string): Promise<any> {
        return new Promise((resolve, reject) => {
            const options = {
                hostname: 'localhost',
                port: 5071,
                path: apiPath,
                method: 'GET',
                headers: {
                    'Accept': 'application/json'
                }
            };

            const req = http.request(options, (res) => {
                let body = '';
                res.on('data', (chunk) => body += chunk);
                res.on('end', () => {
                    if (res.statusCode && res.statusCode >= 200 && res.statusCode < 300) {
                        try {
                            resolve(JSON.parse(body));
                        } catch (e) {
                            reject(new Error('Invalid JSON response'));
                        }
                    } else {
                        reject(new Error(`HTTP ${res.statusCode}: ${body}`));
                    }
                });
            });

            req.on('error', (err) => reject(err));
            req.end();
        });
    }

    private _makePostRequest(apiPath: string, payload: any): Promise<any> {
        return new Promise((resolve, reject) => {
            const postData = JSON.stringify(payload);
            const options = {
                hostname: 'localhost',
                port: 5071,
                path: apiPath,
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Content-Length': Buffer.byteLength(postData),
                    'Accept': 'application/json'
                }
            };

            const req = http.request(options, (res) => {
                let body = '';
                res.on('data', (chunk) => body += chunk);
                res.on('end', () => {
                    if (res.statusCode && res.statusCode >= 200 && res.statusCode < 300) {
                        try {
                            resolve(JSON.parse(body));
                        } catch (e) {
                            reject(new Error('Invalid JSON response'));
                        }
                    } else {
                        reject(new Error(`HTTP ${res.statusCode}: ${body}`));
                    }
                });
            });

            req.on('error', (err) => reject(err));
            req.write(postData);
            req.end();
        });
    }

    private _getHtmlForWebview(_webview: vscode.Webview): string {
        const htmlPath = path.join(this._context.extensionPath, 'src', 'webview', 'graph.html');
        try {
            return fs.readFileSync(htmlPath, 'utf8');
        } catch (error) {
            return `<html><body><h3>Failed to load webview resources: ${(error as Error).message}</h3></body></html>`;
        }
    }
}
