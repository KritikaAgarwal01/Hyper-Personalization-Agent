/*
 * HPSO Contact form scripts.
 *
 * Web resource: ms_/scripts/hpso_contact.js  (JScript)
 * Command bar button on the Contact main form calls: HPSO.Contact.triggerHyperPersonalization
 * Pass "PrimaryControl" as the only parameter.
 *
 * SECURITY NOTE:
 *   FLOW_URL below is a Power Automate "When a HTTP request is received" endpoint whose
 *   trigger is set to "Anyone" and includes a SAS signature (sig=...). Any user who can
 *   read this web resource can extract the URL and invoke the flow. Prefer a Dataverse-
 *   triggered flow (button does Xrm.WebApi.createRecord on a signal table) if that is a
 *   concern. Rotate the URL if it is ever leaked.
 */
var HPSO = HPSO || {};

HPSO.Contact = (function () {
    "use strict";

    // Power Automate HTTP trigger URL for the "Hyperpersonalization Trigger" flow.
    var FLOW_URL = "https://a3ace7c2edb9e4c5bbb7229ad1b767.0b.environment.api.powerplatform.com:443/powerautomate/automations/direct/cu/11/workflows/2f7cf84a944c4ba2ac6208aff5069279/triggers/manual/paths/invoke?api-version=1&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=GD_lPctaEsHInRECXIRmmenYUsSzxz-3wgL1iAHlL1M";

    /**
     * Command bar handler. Sends { contactid: <guid> } to the flow.
     * @param {Xrm.FormContext} primaryControl - pass "PrimaryControl" from the command.
     */
    function triggerHyperPersonalization(primaryControl) {
        var formContext = primaryControl;

        var idRaw = formContext.data.entity.getId();
        if (!idRaw) {
            Xrm.Navigation.openAlertDialog({
                title: "Save required",
                text: "Please save the contact before triggering the HyperPersonalization Journey."
            });
            return;
        }

        var contactId = idRaw.replace(/[{}]/g, "").toLowerCase();
        var payload = JSON.stringify({ contactid: contactId });

        Xrm.Utility.showProgressIndicator("Triggering HyperPersonalization Journey...");

        // Content-Type text/plain keeps this a "simple" CORS request and avoids the
        // OPTIONS preflight that the flow endpoint does not answer. The flow still
        // parses the JSON body from its request schema.
        fetch(FLOW_URL, {
            method: "POST",
            headers: { "Content-Type": "text/plain;charset=UTF-8" },
            body: payload
        }).then(function (response) {
            Xrm.Utility.closeProgressIndicator();
            if (response.ok) {
                Xrm.Navigation.openAlertDialog({
                    title: "Success",
                    text: "HyperPersonalization Journey triggered for this contact."
                });
            } else {
                Xrm.Navigation.openErrorDialog({
                    message: "The flow responded with status " + response.status + "."
                });
            }
        }).catch(function (error) {
            Xrm.Utility.closeProgressIndicator();
            // A CORS restriction on the response can land here even though the request
            // was delivered. Surface a soft message rather than a hard failure.
            Xrm.Navigation.openAlertDialog({
                title: "Request sent",
                text: "The trigger request was submitted. If the journey does not start, check the flow run history. (" + (error && error.message ? error.message : "network") + ")"
            });
        });
    }

    return {
        triggerHyperPersonalization: triggerHyperPersonalization
    };
})();
