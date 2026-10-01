#!/usr/bin/env bash
# Manage head users of the Rilo server dashboard (http://<server>:5080/admin). No restart needed.
#   ./Tools/admin/admin.sh add <name>      # new admin key → copied to your clipboard (also saved in ~/.rilo/admin_keys)
#   ./Tools/admin/admin.sh remove <name>   # revoke that admin's key
#   ./Tools/admin/admin.sh list            # admin names (keys are never printed)
set -euo pipefail
SERVER="${RILO_SERVER:-144.24.139.103}"; KEY="${RILO_SSH_KEY:-$HOME/.ssh/rilo_server.key}"; USER_="${RILO_SSH_USER:-opc}"
SSH=(ssh -i "$KEY" -o ConnectTimeout=15 "$USER_@$SERVER")
cmd="${1:-list}"; name="${2:-}"
case "$cmd" in
  add)
    [[ "$name" =~ ^[A-Za-z0-9_.-]{2,24}$ ]] || { echo "usage: admin.sh add <name> (letters/digits, 2-24)"; exit 1; }
    k=$(head -c 32 /dev/urandom | base64 | tr -d '/+=' | cut -c1-32)
    "${SSH[@]}" "sudo bash -c 'touch /opt/rilo/admins.txt; sed -i \"/^$name:/d\" /opt/rilo/admins.txt; echo \"$name:$k\" >> /opt/rilo/admins.txt; chown rilo:rilo /opt/rilo/admins.txt; chmod 600 /opt/rilo/admins.txt'"
    mkdir -p ~/.rilo && chmod 700 ~/.rilo && touch ~/.rilo/admin_keys && chmod 600 ~/.rilo/admin_keys
    sed -i '' "/^$name:/d" ~/.rilo/admin_keys 2>/dev/null || true
    echo "$name:$k" >> ~/.rilo/admin_keys
    printf %s "$k" | pbcopy
    echo "Admin '$name' added. Their key is in your clipboard (and ~/.rilo/admin_keys)."
    echo "Dashboard: http://$SERVER:5080/admin" ;;
  remove)
    [ -n "$name" ] || { echo "usage: admin.sh remove <name>"; exit 1; }
    "${SSH[@]}" "sudo sed -i \"/^$name:/d\" /opt/rilo/admins.txt"
    sed -i '' "/^$name:/d" ~/.rilo/admin_keys 2>/dev/null || true
    echo "Admin '$name' removed." ;;
  list)
    "${SSH[@]}" "sudo cut -d: -f1 /opt/rilo/admins.txt 2>/dev/null || true" ;;
  *) echo "usage: admin.sh add|remove|list [name]"; exit 1 ;;
esac
