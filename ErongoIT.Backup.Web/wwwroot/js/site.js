(function () {
    "use strict";

    /*
     * Global dashboard monitoring.
     *
     * Do NOT reload the entire browser page.
     *
     * Full-page reloads reset interactive controls such as the
     * Customer, Device and Backup Plan selectors.
     *
     * Backup Jobs instead refreshes only its job-history table.
     */

    const backupJobsPath = "/BackupJobs";
    const refreshIntervalMs = 5000;

    if (window.location.pathname === backupJobsPath) {
        let refreshInProgress = false;

        async function refreshBackupJobs() {
            if (refreshInProgress) {
                return;
            }

            refreshInProgress = true;

            try {
                const response = await fetch(
                    backupJobsPath + "?_refresh=" + Date.now(),
                    {
                        method: "GET",
                        cache: "no-store",
                        headers: {
                            "X-Requested-With": "XMLHttpRequest"
                        }
                    });

                if (!response.ok) {
                    return;
                }

                const html = await response.text();

                const parser = new DOMParser();
                const documentFromServer =
                    parser.parseFromString(html, "text/html");

                const currentTableBody =
                    document.querySelector(".customers-table tbody");

                const newTableBody =
                    documentFromServer.querySelector(".customers-table tbody");

                if (!currentTableBody || !newTableBody) {
                    return;
                }

                currentTableBody.replaceWith(
                    newTableBody.cloneNode(true)
                );

                const currentJobCount =
                    document.querySelector(
                        ".panel-header .badge.badge-muted");

                const newJobCount =
                    documentFromServer.querySelector(
                        ".panel-header .badge.badge-muted");

                if (currentJobCount && newJobCount) {
                    currentJobCount.textContent =
                        newJobCount.textContent;
                }
            }
            catch (error) {
                console.debug(
                    "Backup Jobs live refresh failed.",
                    error);
            }
            finally {
                refreshInProgress = false;
            }
        }

        window.setInterval(
            refreshBackupJobs,
            refreshIntervalMs);
    }
})();
