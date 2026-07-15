#!/usr/bin/env bash
# OATControlX — installation Linux (utilisateur courant, sans sudo)
#
#   ./install.sh              installe (compile en Release self-contained)
#   ./install.sh --uninstall  désinstalle proprement
#
# Fichiers installés :
#   ~/.local/share/oatcontrolx/            application publiée
#   ~/.local/bin/oatcontrolx               lanceur
#   ~/.local/share/applications/oatcontrolx.desktop
#   ~/.local/share/icons/hicolor/256x256/apps/oatcontrolx.png

set -euo pipefail

APP_ID="oatcontrolx"
USER="${USER:-$(id -un)}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
INSTALL_DIR="${HOME}/.local/share/${APP_ID}"
BIN_DIR="${HOME}/.local/bin"
DESKTOP_DIR="${HOME}/.local/share/applications"
ICON_DIR="${HOME}/.local/share/icons/hicolor/256x256/apps"

bold() { printf '\033[1m%s\033[0m\n' "$*"; }
info() { printf '  %s\n' "$*"; }
warn() { printf '\033[33m! %s\033[0m\n' "$*"; }

uninstall() {
	bold "Désinstallation d'OATControlX..."
	rm -rf "${INSTALL_DIR}"
	rm -f "${BIN_DIR}/${APP_ID}"
	rm -f "${DESKTOP_DIR}/${APP_ID}.desktop"
	rm -f "${ICON_DIR}/${APP_ID}.png"
	command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "${DESKTOP_DIR}" || true
	bold "Terminé. (Les réglages dans ~/.config/OpenAstroTracker sont conservés.)"
	exit 0
}

[ "${1:-}" = "--uninstall" ] && uninstall

# ---------------------------------------------------------------- SDK .NET 8
bold "1/5 Vérification du SDK .NET 8..."
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
	warn "SDK .NET 8 introuvable."
	info "Ubuntu/Debian : sudo apt install dotnet-sdk-8.0"
	info "Fedora        : sudo dnf install dotnet-sdk-8.0"
	info "Arch          : sudo pacman -S dotnet-sdk"
	info "Autre         : https://dotnet.microsoft.com/download/dotnet/8.0"
	exit 1
fi
info "OK : $(dotnet --version)"

# ---------------------------------------------------------------- publication
bold "2/5 Compilation (Release, self-contained linux-x64)..."
PUBLISH_DIR="$(mktemp -d)"
trap 'rm -rf "${PUBLISH_DIR}"' EXIT
dotnet publish "${SCRIPT_DIR}/OATControlX.csproj" -c Release -r linux-x64 --self-contained \
	-o "${PUBLISH_DIR}" -v quiet -nologo
info "OK"

# ---------------------------------------------------------------- fichiers
bold "3/5 Installation dans ${INSTALL_DIR}..."
rm -rf "${INSTALL_DIR}"
mkdir -p "${INSTALL_DIR}" "${BIN_DIR}" "${DESKTOP_DIR}" "${ICON_DIR}"
cp -r "${PUBLISH_DIR}/." "${INSTALL_DIR}/"
ln -sf "${INSTALL_DIR}/OATControlX" "${BIN_DIR}/${APP_ID}"
cp "${SCRIPT_DIR}/Assets/oatcontrolx.png" "${ICON_DIR}/${APP_ID}.png"
info "OK"

# ---------------------------------------------------------------- menu
bold "4/5 Entrée de menu..."
cat > "${DESKTOP_DIR}/${APP_ID}.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=OATControlX
GenericName=OpenAstroTech Setup Utility
Comment=Mise en service, calibration et diagnostic des montures OpenAstroTracker/OpenAstroExplorer
Exec=${INSTALL_DIR}/OATControlX
Icon=${APP_ID}
Terminal=false
Categories=Science;Astronomy;
Keywords=telescope;astronomy;OAT;OAE;mount;
EOF
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "${DESKTOP_DIR}" || true
info "OK"

# ---------------------------------------------------------------- port série
bold "5/5 Vérification de l'accès au port série..."
if id -nG "$USER" | grep -qw dialout; then
	info "OK : $USER est dans le groupe dialout."
else
	warn "$USER n'est PAS dans le groupe dialout — l'accès à /dev/ttyUSB* sera refusé."
	info "Corrige avec :  sudo usermod -aG dialout $USER"
	info "puis déconnecte/reconnecte ta session."
fi

echo
bold "Installation terminée !"
info "Lancement : ${APP_ID} (ou via le menu Applications > Science)"
if ! echo ":${PATH}:" | grep -q ":${BIN_DIR}:"; then
	warn "${BIN_DIR} n'est pas dans ton PATH ; ajoute-le ou lance ${INSTALL_DIR}/OATControlX"
fi
