import { initViewer, loadModel } from './viewer.js';



async function setupModelSelection(viewer, selectedUrn) {
    const dropdown = document.getElementById('models');
    dropdown.innerHTML = '';
    try {
        const resp = await fetch('/api/models');
        if (!resp.ok) {
            throw new Error(await resp.text());
        }
        const models = await resp.json();
        dropdown.innerHTML = models.map(model => `<option value=${model.urn} ${model.urn === selectedUrn ? 'selected' : ''}>${model.name}</option>`).join('\n');
        dropdown.onchange = () => onModelSelected(viewer, dropdown.value);
        if (dropdown.value) {
            onModelSelected(viewer, dropdown.value);
        }
    } catch (err) {
        alert('Could not list models. See the console for more details.');
        console.error(err);
    }
}

document.addEventListener('DOMContentLoaded', async function () {
    prepareLists();

    document.getElementById('clearAccount').addEventListener('click', clearAccount);
    document.getElementById('defineActivityModal').addEventListener('click', defineActivityModal);
    document.getElementById('createAppBundleActivity').addEventListener('click', createAppBundleActivity);
    document.getElementById('startWorkitem').addEventListener('click', startWorkitem);

    await startConnection();
});

async function prepareLists() {
    await list('activity', '/api/designautomation/activities');
    await list('engines', '/api/designautomation/engines');
    await list('localBundles', '/api/designautomation/apps');
}

async function list(control, endpoint) {
    var selectElement = document.getElementById(control);
    selectElement.innerHTML = '';
    const response = await fetch(endpoint);
    const list = await response.json();
    if (list.length === 0) {
        var option = document.createElement('option');
        option.disabled = true;
        option.text = 'Nothing found';
        selectElement.appendChild(option);
    } else {
        list.forEach(function (item) {
            var option = document.createElement('option');
            option.value = item;
            option.text = item;
            selectElement.appendChild(option);
        });
    }
}

async function clearAccount() {
    if (!confirm('Clear existing activities & appbundles before start. ' +
        'This is useful if you believe there are wrong settings on your account.' +
        '\n\nYou cannot undo this operation. Proceed?')) return;

    await fetch('api/designautomation/account', { method: 'DELETE' });
    prepareLists();
    writeLog('Account cleared, all appbundles & activities deleted');
}

function defineActivityModal() {
    document.getElementById("defineActivityModal").classList.add('show');
}

async function createAppBundleActivity() {
    await startConnection();
    writeLog("Defining appbundle and activity for " + document.getElementById('engines').value);
    document.getElementById("defineActivityModal").classList.remove('show');
    await createAppBundle();
    await createActivity();
    prepareLists();
}

async function createAppBundle() {
    const response = await fetch('api/designautomation/appbundles', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            zipFileName: document.getElementById('localBundles').value,
            engine: document.getElementById('engines').value
        })
    });
    const res = await response.json();
    writeLog('AppBundle: ' + res.appBundle + ', v' + res.version);
}

async function createActivity() {
    const response = await fetch('api/designautomation/activities', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            zipFileName: document.getElementById('localBundles').value,
            engine: document.getElementById('engines').value
        })
    });
    const res = await response.json();
    writeLog('Activity: ' + res.activity);
}

async function startWorkitem() {
    var inputFileField = document.getElementById('inputFile');
    if (inputFileField.files.length === 0) { alert('Please select an input file'); return; }
    if (document.getElementById('activity').value === '') { alert('Please select an activity'); return; }
    var file = inputFileField.files[0];
    await startConnection();
    var formData = new FormData();
    formData.append('inputFile', file);
    formData.append('data', JSON.stringify({
        width: document.getElementById('width').value,
        height: document.getElementById('height').value,
        activityName: document.getElementById('activity').value,
        browserConnectionId: connectionId
    }));
    writeLog('Uploading input file...');
    const response = await fetch('api/designautomation/workitems', {
        method: 'POST',
        body: formData
    });
    const res = await response.json();
    writeLog('Workitem started: ' + res.workItemId);
}

function writeLog(text) {
    var outputlog = document.getElementById('outputlog');
    outputlog.innerHTML += '<div style="border-top: 1px dashed #C0C0C0">' + text + '</div>';
    outputlog.scrollTop = outputlog.scrollHeight;
}

var connection;
var connectionId;

async function startConnection() {
    if (connection && connection.connectionState) return;
    connection = new signalR.HubConnectionBuilder().withUrl("/api/signalr/designautomation").build();
    await connection.start();
    const id = await connection.invoke('getConnectionId');
    connectionId = id;
    connection.on("downloadResult", function (url) {
        writeLog('<a href="' + url + '">Download result file here</a>');
    });

    connection.on("onComplete", function (message) {
        writeLog(message);
    });

    connection.on("onTranslation", function (urn) {
        writeLog('Translation complete, URN: ' + urn);
        initViewer(document.getElementById('preview')).then(viewer => {            
            loadModel(viewer, urn);
        });
    });
}
