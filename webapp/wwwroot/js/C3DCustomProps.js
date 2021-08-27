/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

// Based on the extesion library
// this extensions requires connection to a database
// https://github.com/Autodesk-Forge/forge-extensions/tree/master/public/extensions/CustomPropertiesExtension

// *******************************************
// Custom Property Panel
// *******************************************
class CustomPropertyPanel extends Autodesk.Viewing.Extensions.ViewerPropertyPanel {
  constructor(viewer, options) {
    super(viewer, options);
    this._data = null;
  }
  setAggregatedProperties(properties, options) {
    Autodesk.Viewing.Extensions.ViewerPropertyPanel.prototype.setAggregatedProperties.call(this, properties, options);

    if (this._data != null) {
      this.viewer.getProperties(this.propertyNodeId, (props) => {
        const handle = props.externalId;

        this._data.forEach((d) => {
          if (d.handle !== handle) return;

          d.style.forEach((s) => {
            this.addProperty(s.name, s.value, 'Style');
          })
        })
      })
    }
  }

  setNodeProperties(nodeId) {
    Autodesk.Viewing.Extensions.ViewerPropertyPanel.prototype.setNodeProperties.call(this, nodeId);
    this.nodeId = nodeId; // store the dbId for later use
  }
}

// *******************************************
// Custom Property Panel Extension
// *******************************************
class CustomPropertyPanelExtension extends Autodesk.Viewing.Extension {
  constructor(viewer, options) {
    super(viewer, options);
    this._panel = null;
  }

  async load() {
    this.startConnection(() => {
      var params = this.options.itemId.split('/');
      this.options.projectId = params[params.length - 3];

      $.notify("Requesting style information... please wait.", "info");
      jQuery.post({
        url: '/api/styles',
        contentType: 'application/json',
        data: JSON.stringify({ 'connectionId': window._connectionId, 'activity': 'extractStyles', 'itemId': this.options.itemId, 'versionId': this.options.versionId }),
        success: function (res, status) {
          switch (status) {
            case "nocontent":
              // the information will be sent via websocket
              $.notify("File already processed, using cached version.", "info");
              break;
            case "success":
              $.notify("Design Automation Workitem started... please wait.", "info");
              break;
          }
        },
        error: function (err) {
          $.notify("Fail to trigger Design Automation to extract style information", "error");
        }
      });
    });

    this.viewer.addEventListener(Autodesk.Viewing.EXTENSION_LOADED_EVENT, async (e) => {
      if (e.extensionId !== 'Autodesk.PropertiesManager') return;
      this._panel = new CustomPropertyPanel(this.viewer, this.options);
      var ext = await this.viewer.getExtension('Autodesk.PropertiesManager');
      ext.setPanel(this._panel);
    })

    return true;
  }

  async unload() {
    if (this._panel == null) return;
    var ext = await this.viewer.getExtension('Autodesk.PropertiesManager');
    this._panel = null;
    ext.setDefaultPanel();
    return true;
  }

  startConnection(onReady) {
    if (window._connection != undefined && window._connection.connectionState) { if (onReady) onReady(); return; }
    window._connection = new signalR.HubConnectionBuilder().withUrl("/api/signalr/designautomation").build();
    window._connection.start()
      .then(() => {
        window._connection.invoke('getConnectionId')
          .then((id) => {
            window._connectionId = id; // we'll need this...
            this.defineHandles();
            if (onReady) onReady();
          });
      });
  }

  defineHandles() {
    window._connection.on("propsReady", (url) => {
      jQuery.get({
        url: url,
        success: (res) => {
          viewer.getExtension('Autodesk.PropertiesManager').getPanel()._data = JSON.parse(res);
          $.notify("Style information ready to use.", "success");
        },
        error: (err) => {

        }
      });
    });

    window._connection.on("onCompleteProps", (workItemStatus) => {
      console.log(workItemStatus);
      workItemStatus = JSON.parse(workItemStatus);
      if (workItemStatus.status !== 'success')
        $.notify("Fail to extract style information", "error");
    });
  }
}

Autodesk.Viewing.theExtensionManager.registerExtension('Autodesk.Sample.CustomPropertyPanelExtension', CustomPropertyPanelExtension);