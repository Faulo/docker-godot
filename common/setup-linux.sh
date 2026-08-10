#!/bin/sh
set -eu

godot_root=/godot/binaries
template_root=/godot/export_templates
blender_root=/blender
state_root=/run/docker-godot

log() {
    printf '%s\n' "docker-godot: $*" >&2
}

fail() {
    log "$*"
    exit 1
}

validate_selector() {
    selector_name="$1"
    selector_value="$2"
    printf '%s' "$selector_value" | grep -Eq '^[0-9]+(\.[0-9]+){0,2}$' || \
        fail "$selector_name must contain one to three numeric components, got: $selector_value"
}

matches_selector() {
    candidate="$1"
    selector="$2"
    case "$selector" in
        *.*.*) [ "$candidate" = "$selector" ] ;;
        *.*) [ "${candidate%.*}" = "$selector" ] ;;
        *) [ "${candidate%%.*}" = "$selector" ] ;;
    esac
}

resolve_godot() {
    selector="$1"
    page=1
    candidates=""
    while [ "$page" -le 10 ]; do
        response="$(curl -fsSL --retry 5 \
            -H 'Accept: application/vnd.github+json' \
            -H 'User-Agent: docker-godot' \
            "https://api.github.com/repos/godotengine/godot-builds/releases?per_page=100&page=$page")" || \
            fail "failed to query Godot releases"
        count="$(printf '%s' "$response" | jq 'length')"
        tags="$(printf '%s' "$response" | jq -r '.[].tag_name | select(test("^[0-9]+\\.[0-9]+(\\.[0-9]+)?-stable$"))')"
        for tag in $tags; do
            version="${tag%-stable}"
            case "$version" in
                *.*.*) normalized="$version" ;;
                *.*) normalized="$version.0" ;;
            esac
            if matches_selector "$normalized" "$selector"; then
                candidates="${candidates}${candidates:+\n}${normalized}|${tag}"
            fi
        done
        [ -z "$candidates" ] || break
        [ "$count" -eq 100 ] || break
        page=$((page + 1))
    done
    [ -n "$candidates" ] || fail "no stable Godot release matches GODOT_VERSION=$selector"
    printf '%b\n' "$candidates" | sort -t '|' -k1,1V | tail -n1
}

resolve_blender() {
    selector="$1"
    major="${selector%%.*}"
    root_listing="$(curl -fsSL --retry 5 'https://download.blender.org/release/')" || \
        fail "failed to query Blender releases"
    series_list="$(printf '%s' "$root_listing" | \
        grep -oE "Blender${major}\.[0-9]+/" | sort -Vu)"
    [ -n "$series_list" ] || fail "no stable Blender release matches BLENDER_VERSION=$selector"

    candidates=""
    for series in $series_list; do
        minor="$(printf '%s' "$series" | sed -E 's/^Blender[0-9]+\.([0-9]+)\/$/\1/')"
        case "$selector" in
            *.*) requested_minor="$(printf '%s' "$selector" | cut -d. -f2)"; [ "$minor" = "$requested_minor" ] || continue ;;
        esac
        listing="$(curl -fsSL --retry 5 "https://download.blender.org/release/$series")" || \
            fail "failed to query Blender series $series"
        versions="$(printf '%s' "$listing" | \
            grep -oE "blender-${major}\.${minor}\.[0-9]+-linux-x64\.tar\.xz" | \
            sed -E 's/^blender-([0-9]+\.[0-9]+\.[0-9]+)-.*$/\1/' | sort -Vu)"
        for version in $versions; do
            if matches_selector "$version" "$selector"; then
                candidates="${candidates}${candidates:+\n}${version}|${series}"
            fi
        done
    done
    [ -n "$candidates" ] || fail "no stable Blender release matches BLENDER_VERSION=$selector"
    printf '%b\n' "$candidates" | sort -t '|' -k1,1V | tail -n1
}

download() {
    url="$1"
    destination="$2"
    attempt=1
    while [ "$attempt" -le 5 ]; do
        if curl -fsSL --http1.1 --continue-at - "$url" -o "$destination"; then
            return
        fi
        [ "$attempt" -lt 5 ] || fail "download failed after $attempt attempts: $url"
        log "download interrupted; resuming attempt $((attempt + 1)) of 5"
        sleep $((attempt * 2))
        attempt=$((attempt + 1))
    done
}

verify_sha512() {
    archive="$1"
    sums="$2"
    filename="$(basename "$archive")"
    expected="$(awk -v name="$filename" '$2 == name || $2 == "*" name { print $1; exit }' "$sums")"
    [ -n "$expected" ] || fail "missing SHA-512 checksum for $filename"
    actual="$(sha512sum "$archive" | awk '{ print $1 }')"
    [ "$actual" = "$expected" ] || fail "SHA-512 checksum mismatch for $filename"
}

verify_sha256() {
    archive="$1"
    sums="$2"
    filename="$(basename "$archive")"
    expected="$(awk -v name="$filename" '$2 == name || $2 == "*" name { print $1; exit }' "$sums")"
    [ -n "$expected" ] || fail "missing SHA-256 checksum for $filename"
    actual="$(sha256sum "$archive" | awk '{ print $1 }')"
    [ "$actual" = "$expected" ] || fail "SHA-256 checksum mismatch for $filename"
}

ensure_godot_locked() {
    selector="${GODOT_VERSION:-}"
    [ -n "$selector" ] || fail 'GODOT_VERSION is required'
    validate_selector GODOT_VERSION "$selector"
    [ "${selector%%.*}" -eq 4 ] || fail 'only standard Godot 4 releases are currently supported'
    resolved="$(resolve_godot "$selector")"
    normalized="${resolved%%|*}"
    tag="${resolved#*|}"
    install_id="$(printf '%s' "$tag" | tr '-' '.')"
    install_dir="$godot_root/$install_id"
    template_dir="$template_root/$install_id"
    executable="$install_dir/Godot_v${tag}_linux.x86_64"

    if [ ! -x "$executable" ]; then
        log "installing Godot $normalized ($install_id)"
        find "$godot_root" -mindepth 1 -maxdepth 1 -type d -name ".$install_id.*" -exec rm -rf -- {} +
        temporary="$(mktemp -d "$godot_root/.${install_id}.XXXXXX")"
        trap 'rm -rf "$temporary"' 0
        trap 'exit 130' 1 2 15
        archive="$temporary/Godot_v${tag}_linux.x86_64.zip"
        sums="$temporary/SHA512-SUMS.txt"
        base_url="https://github.com/godotengine/godot-builds/releases/download/$tag"
        download "$base_url/$(basename "$archive")" "$archive"
        download "$base_url/SHA512-SUMS.txt" "$sums"
        verify_sha512 "$archive" "$sums"
        unzip -q "$archive" -d "$temporary/unpacked"
        chmod +x "$temporary/unpacked/Godot_v${tag}_linux.x86_64"
        mv "$temporary/unpacked" "$install_dir"
        rm -rf "$temporary"
        trap - 0 1 2 15
    fi

    if [ ! -f "$template_dir/version.txt" ]; then
        log "installing Godot export templates $install_id"
        find "$template_root" -mindepth 1 -maxdepth 1 -type d -name ".$install_id.*" -exec rm -rf -- {} +
        temporary="$(mktemp -d "$template_root/.${install_id}.XXXXXX")"
        trap 'rm -rf "$temporary"' 0
        trap 'exit 130' 1 2 15
        archive="$temporary/Godot_v${tag}_export_templates.tpz"
        sums="$temporary/SHA512-SUMS.txt"
        base_url="https://github.com/godotengine/godot-builds/releases/download/$tag"
        download "$base_url/$(basename "$archive")" "$archive"
        download "$base_url/SHA512-SUMS.txt" "$sums"
        verify_sha512 "$archive" "$sums"
        unzip -q "$archive" -d "$temporary/unpacked"
        [ -d "$temporary/unpacked/templates" ] || fail "Godot template archive has an unexpected layout"
        mv "$temporary/unpacked/templates" "$template_dir"
        rm -rf "$temporary"
        trap - 0 1 2 15
    fi

    printf '%s\n' "$executable"
}

ensure_blender_locked() {
    selector="${BLENDER_VERSION:-}"
    [ -n "$selector" ] || fail 'BLENDER_VERSION is required'
    validate_selector BLENDER_VERSION "$selector"
    resolved="$(resolve_blender "$selector")"
    version="${resolved%%|*}"
    series="${resolved#*|}"
    install_dir="$blender_root/$version"
    executable="$install_dir/blender"

    if [ ! -x "$executable" ]; then
        log "installing Blender $version"
        find "$blender_root" -mindepth 1 -maxdepth 1 -type d -name ".$version.*" -exec rm -rf -- {} +
        temporary="$(mktemp -d "$blender_root/.${version}.XXXXXX")"
        trap 'rm -rf "$temporary"' 0
        trap 'exit 130' 1 2 15
        filename="blender-${version}-linux-x64.tar.xz"
        archive="$temporary/$filename"
        sums="$temporary/blender-${version}.sha256"
        base_url="https://download.blender.org/release/$series"
        download "$base_url/$filename" "$archive"
        download "$base_url/blender-${version}.sha256" "$sums"
        verify_sha256 "$archive" "$sums"
        mkdir "$temporary/unpacked"
        tar -xJf "$archive" -C "$temporary/unpacked" --strip-components=1
        mv "$temporary/unpacked" "$install_dir"
        rm -rf "$temporary"
        trap - 0 1 2 15
    fi

    printf '%s\n' "$executable"
}

with_lock() {
    root="$1"
    shift
    mkdir -p "$root"
    (
        flock -x 9
        "$@"
    ) 9>"$root/.docker-godot.lock"
}

with_godot_locks() {
    mkdir -p "$godot_root" "$template_root"
    (
        flock -x 9
        flock -x 8
        "$@"
    ) 9>"$godot_root/.docker-godot.lock" 8>"$template_root/.docker-godot.lock"
}

write_ready() {
    mkdir -p "$state_root"
    temporary="$state_root/ready.$$"
    : > "$temporary"
    for path in "$@"; do
        printf '%s\n' "$path" >> "$temporary"
    done
    mv "$temporary" "$state_root/ready"
}

configure_blender_for_godot() {
    godot_path="$1"
    install_id="$(basename "$(dirname "$godot_path")")"
    major="${install_id%%.*}"
    remainder="${install_id#*.}"
    minor="${remainder%%.*}"
    settings_root="${XDG_CONFIG_HOME:-${HOME:-/root}/.config}/godot"
    settings_file=""
    candidate_minor="$minor"
    while [ "$candidate_minor" -ge 3 ]; do
        candidate="$settings_root/editor_settings-$major.$candidate_minor.tres"
        if [ -f "$candidate" ]; then
            settings_file="$candidate"
            break
        fi
        candidate_minor=$((candidate_minor - 1))
    done
    legacy_settings="$settings_root/editor_settings-$major.tres"
    if [ -z "$settings_file" ] && { [ "$minor" -lt 3 ] || [ -f "$legacy_settings" ]; }; then
        settings_file="$legacy_settings"
    fi
    [ -n "$settings_file" ] || settings_file="$settings_root/editor_settings-$major.$minor.tres"
    setting='filesystem/import/blender/blender_path = "/usr/local/bin/blender"'
    mkdir -p "$settings_root"
    if [ ! -f "$settings_file" ]; then
        printf '[gd_resource type="EditorSettings" format=3]\n\n[resource]\n%s\n' "$setting" > "$settings_file"
    elif grep -q '^filesystem/import/blender/blender_path = ' "$settings_file"; then
        temporary="$settings_file.$$"
        sed "s|^filesystem/import/blender/blender_path = .*$|$setting|" "$settings_file" > "$temporary"
        mv "$temporary" "$settings_file"
    else
        printf '%s\n' "$setting" >> "$settings_file"
    fi
}

health() {
    ready="$state_root/ready"
    [ -s "$ready" ] || exit 1
    while IFS= read -r path; do
        [ -x "$path" ] || exit 1
    done < "$ready"
}

case "${1:-}" in
    godot)
        paths=""
        if [ -n "${BLENDER_VERSION:-}" ]; then
            blender_path="$(with_lock "$blender_root" ensure_blender_locked)"
            paths="$blender_path"
            mkdir -p "$state_root"
            printf '%s\n%s\n' "$BLENDER_VERSION" "$blender_path" > "$state_root/blender"
        fi
        godot_path="$(with_godot_locks ensure_godot_locked)"
        if [ -n "$paths" ]; then
            configure_blender_for_godot "$godot_path"
        fi
        if [ -n "$paths" ]; then
            write_ready "$paths" "$godot_path"
        else
            write_ready "$godot_path"
        fi
        printf '%s\n' "$godot_path"
        ;;
    blender)
        cached_blender="$state_root/blender"
        cached_selector="$(sed -n '1p' "$cached_blender" 2>/dev/null || true)"
        cached_path="$(sed -n '2p' "$cached_blender" 2>/dev/null || true)"
        if [ "$cached_selector" = "${BLENDER_VERSION:-}" ] && [ -x "$cached_path" ]; then
            blender_path="$cached_path"
        else
            blender_path="$(with_lock "$blender_root" ensure_blender_locked)"
            mkdir -p "$state_root"
            printf '%s\n%s\n' "$BLENDER_VERSION" "$blender_path" > "$cached_blender"
        fi
        write_ready "$blender_path"
        printf '%s\n' "$blender_path"
        ;;
    health)
        health
        ;;
    *)
        fail 'usage: setup godot|blender|health'
        ;;
esac
