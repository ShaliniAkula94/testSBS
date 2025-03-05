import logging
from pathlib import Path
import yaml
import requests
import json
import os

logging.basicConfig(level=logging.DEBUG)

# Request timeout
TIMEOUT = 20

# API token and Subdomain are set as env variables.
# It is adviced not to hard code sensitive information in your code.
LEANIX_API_TOKEN = "wm6X3aYuJX4ZLKpOufgDz5zt8LVAucGcL77WALL6"
LEANIX_SUBDOMAIN = "leanix"
LEANIX_FQDN = f"https://{LEANIX_SUBDOMAIN}.leanix.net/services"

# OAuth2 URL to request the access token.
LEANIX_OAUTH2_URL = f"{LEANIX_FQDN}/mtm/v1/oauth2/token"

# Manifest API
MANIFEST_API = f"{LEANIX_FQDN}/technology-discovery/v1/manifests"

# Github Related
#GITHUB_SERVER_URL = os.getenv("GITHUB_SERVER_URL")
#GITHUB_REPOSITORY = os.getenv("GITHUB_REPOSITORY")

# Manifest file and SBOM file
LEANIX_MANIFEST_FILE = os.getenv("LEANIX_MANIFEST_FILE", "leanix.yaml")
#SBOM_FILE = "CBRE.xml"


def _ensure_file(file: Path):
    """Ensures that the provided file exists and is a file.

    Args:
        file (Path): The path to the file.

    Raises:
        FileNotFoundError: If the file does not exist or is not a file.
    """
    if not (file.exists() and file.is_file()):
        raise FileNotFoundError(f"File {file} not found")


def _obtain_access_token() -> str:
    """Obtains a LeanIX Access token using the Technical User generated
    API secret.

    Returns:
        str: The LeanIX Access Token
    """
    if not LEANIX_API_TOKEN:
        raise Exception("A valid token is required")
    response = requests.post(
        LEANIX_OAUTH2_URL,
        auth=("apitoken", LEANIX_API_TOKEN),
        data={"grant_type": "client_credentials"},
    )
    response.raise_for_status()
    return response.json().get("access_token")


def _prepare_auth() -> dict:
    """
    Prepares the headers for a GraphQL request to the LeanIX API.

    This function fetches the LeanIX access token from the environment variables and constructs
    the authorization header required for making authenticated requests to the LeanIX API.

        dict: A dictionary containing the authorization header with the access token.
    """
    # Fetch the access token and set the Authorization Header
    auth_header = f'Bearer {os.environ.get("LEANIX_ACCESS_TOKEN")}'
    # Provide the headers
    headers = {
        "Authorization": auth_header,
    }
    return headers


def create_or_update_micro_services(manifest_file: Path):
    """
    Creates or updates the LeanIX Microservice Fact Sheet based on the provided manifest file.

    This function checks if a microservice with the given external ID exists. If it does, the microservice is updated.
    If it does not exist, a new microservice is created. After the microservice is created or updated,
    the function triggers the registration of the relevant SBOM file with LeanIX.

    Args:
        manifest_file (Path): The SAP LeanIX manifest file.
    """
    logging.info(
        f"Processing manifest file: {manifest_file.name}"
    )

    # NOTE: application/yaml here does not mean the content type, but the type of the file.
    request_payload = {
        "file": (
            manifest_file.name,
            manifest_file.open("rb"),
            "application/yaml",
        )
    }
    auth = _prepare_auth()
    resp = requests.put(
        url=MANIFEST_API,
        headers=auth,
        files=request_payload,
        timeout=TIMEOUT,
    )
    logging.debug(f"Response: {resp.status_code} - {resp.text}")
    resp.raise_for_status()
    logging.debug(f"Response Status Code: {resp.status_code}")
    logging.debug(f"Response Content: {resp.content}")
    logging.debug(f" Response: {resp.status_code} - {resp.text}")
    logging.info(f"Successfully uploaded manifest file: {manifest_file.name}")
    factsheet_id = resp.json().get("data").get("factSheetId")
    if not factsheet_id:
        raise Exception("Service did not return a fact sheet ID")
    register_sboms(factsheet_id)


def register_sboms(factsheet_id: str):
    """
    Registers the Software Bill of Materials (SBOM) file with LeanIX.

    This function enables improved understanding of the dependency landscape of your microservices.
    The SBOM provides comprehensive details about software components, their relationships, and
    attributes, which are crucial for managing, securing, and licensing your open-source software.
    By registering the SBOM with LeanIX, these details can be effectively managed and tracked.

    Args:
        factsheet_id (str): The unique identifier of the microservice fact sheet. This ID is used
        to associate the SBOM with the corresponding microservice in LeanIX.

    Returns:
        None
    """
    sbom_path = Path(SBOM_FILE)
    # NOTE: If SBOMs are mandatory for your organization, modify this to raise an exception.
    try:
        _ensure_file(sbom_path)
    except FileNotFoundError:
        logging.warning("No sbom file found")
        return

    sbom_endpoint = f"{LEANIX_FQDN}/technology-discovery/v1/factSheets/{factsheet_id}/sboms"
    sbom_contents = dict()
    logging.info(
        f"Processing sbom file: {sbom_path.name} for Fact Sheet: {factsheet_id}"
    )
    with sbom_path.open("rb") as f:
        sbom_contents = f.read()

    # NOTE: application/json here does not mean the content type, but the type of the file.
    request_payload = {
        "sbom": (
            sbom_path.name,
            sbom_contents,
            "application/json",
        )
    }
    logging.debug(f"Populated payload for SBOM: {sbom_path.name}")
    # Fetch the access token and set the Authorization Header
    auth_header = _prepare_auth()
    # NOTE: Don't set the content type, `requests` should handle this.
    logging.info(f"Sending SBOM ingestion request for Fact Sheet: {factsheet_id}")
    response = requests.post(
        sbom_endpoint, headers=auth_header, files=request_payload, timeout=TIMEOUT
    )
    logging.debug(f" Response: {response.status_code} - {response.text}")
    response.raise_for_status()
    logging.debug(f"Response Status Code: {response.status_code}")
    logging.debug(f"Response Content: {response.content}")
    logging.debug(f" Response: {response.status_code} - {response.text}")
    #logging.info(f"Successfully submited SBOM request for Fact Sheet: {factsheet_id}")


def main():
    """LeanIX helper to parse the manifest file create or update a microservice
    and register the relevant dependencies.
    """
    manifest_file = Path(LEANIX_MANIFEST_FILE)
    _ensure_file(manifest_file)
    create_or_update_micro_services(manifest_file)


if __name__ == "__main__":
    # Set the access token as an environment variable
    os.environ["LEANIX_ACCESS_TOKEN"] = _obtain_access_token()
    main()
