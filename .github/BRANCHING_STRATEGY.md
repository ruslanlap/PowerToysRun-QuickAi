# Branching Strategy

## Overview

This project uses a **GitFlow-inspired** branching strategy for safe testing and releases.

```
Contributors → PR to dev → Preview Release → Merge to master → Production Release
```

## Branch Structure

### `master` (Production)
- **Purpose**: Stable, production-ready code
- **Protection**: Protected branch, requires PR approval
- **Releases**: Only official releases (v1.0.0, v1.1.0, v1.2.0)
- **Merges from**: `dev` branch only (after testing)
- **Workflow**: `build-and-release-optimized.yml` (on version tags)

### `dev` (Development/Integration)
- **Purpose**: Integration branch for testing new features
- **Protection**: Protected branch, requires PR approval
- **Releases**: Preview/beta releases (v1.2.0-dev-abc1234)
- **Merges from**: Feature branches, external PRs
- **Workflow**: `build-dev-preview.yml` (automatic on push)

### Feature Branches
- **Naming**: `feature/description`, `fix/issue-number`, `enhancement/name`
- **Purpose**: Individual features or bug fixes
- **Created from**: `dev` branch
- **Merged to**: `dev` branch via PR
- **Lifetime**: Short-lived, deleted after merge

## Workflow for Contributors

### External Contributors (First-time or community)

1. **Fork the repository**
2. **Create feature branch** from latest `master`:
   ```bash
   git checkout master
   git pull origin master
   git checkout -b feature/my-feature
   ```
3. **Make changes and commit**
4. **Create PR targeting `dev` branch** (not `master`!)
   - PR will automatically trigger dev preview build
   - Maintainer will review and test
5. **After approval**: PR merged to `dev`

### Maintainers (Project owners)

1. **Review PRs to `dev`**:
   - Check security using `.github/PR_SECURITY_CHECKLIST.md`
   - Test dev preview build from GitHub Actions
   - Verify functionality
   - Request changes if needed

2. **Merge to `dev`**:
   - Use "Squash and merge" for clean history
   - Delete feature branch after merge

3. **Test `dev` branch**:
   - Use dev preview releases
   - Gather feedback
   - Fix any issues

4. **Promote to `master`**:
   ```bash
   git checkout master
   git pull origin master
   git merge --no-ff dev -m "Release v1.2.0: Description"
   git tag v1.2.0
   git push origin master --tags
   ```
   - This triggers production release workflow

## Release Process

### Preview Releases (from `dev`)

**Triggered by**: Push to `dev` branch or PR to `dev`

**Version format**: `1.2.0-dev-abc1234` or `1.2.0-pr5-abc1234`

**Steps**:
1. PR merged to `dev` (or direct push by maintainer)
2. GitHub Actions runs `build-dev-preview.yml`
3. Builds artifacts for x64 and ARM64
4. Uploads to Actions artifacts (14-day retention)
5. Comments on PR with download links and testing checklist
6. **No GitHub Release created** (artifacts only)

**Testing**:
- Download artifacts from Actions run
- Install locally and test
- Report issues as comments on PR or new issues
- If critical bugs found, fix in `dev` before promoting to `master`

### Production Releases (from `master`)

**Triggered by**: Version tag on `master` branch

**Version format**: `1.2.0` (semver)

**Steps**:
1. All testing completed on `dev` preview
2. Merge `dev` to `master` with `--no-ff`
3. Create version tag:
   ```bash
   git tag -a v1.2.0 -m "Release v1.2.0: Feature description"
   git push origin v1.2.0
   ```
4. GitHub Actions runs `build-and-release-optimized.yml`
5. Creates official GitHub Release with:
   - Release notes
   - x64 and ARM64 ZIPs
   - SHA256 checksums
6. Updates README.md download links (manual)
7. Announces release (optional)

## Branch Protection Rules

### For `master`:
```yaml
Required reviews: 1
Dismiss stale reviews: true
Require review from code owners: true
Require status checks: true
  - Build (x64)
  - Build (ARM64)
Require branches to be up to date: true
Include administrators: false
```

### For `dev`:
```yaml
Required reviews: 1
Dismiss stale reviews: false
Require status checks: true
  - Build (x64)
  - Build (ARM64)
Include administrators: false
```

## Setting Up GitHub Branch Protection

1. Go to **Settings** → **Branches**
2. Click **Add rule** for `master`:
   - Branch name pattern: `master`
   - ✅ Require pull request reviews before merging
   - ✅ Require status checks to pass before merging
   - Select: `build (x64)`, `build (ARM64)`
   - ✅ Require branches to be up to date before merging
   - ✅ Do not allow bypassing the above settings

3. Click **Add rule** for `dev`:
   - Branch name pattern: `dev`
   - ✅ Require pull request reviews before merging
   - ✅ Require status checks to pass before merging
   - Select: `build (x64)`, `build (ARM64)`

## Creating the `dev` Branch

```bash
# From your current master branch
git checkout master
git pull origin master

# Create dev branch
git checkout -b dev

# Push to remote
git push -u origin dev

# Set dev as default branch for PRs (optional)
# GitHub Settings → General → Default branch → dev
```

## Updating Workflows for Dev Branch

The `build-dev-preview.yml` workflow already supports this strategy. It will:
- Build on push to `dev` branch
- Build on PRs targeting `dev` or `master`
- Create preview releases with proper versioning
- Comment on PRs with testing instructions

## Example Scenario

### Scenario: External user adds new feature

1. **User creates PR**:
   - User: `git checkout -b feature/streaming-window`
   - User: Makes changes, commits
   - User: Creates PR targeting `dev` branch

2. **Auto preview build**:
   - GitHub Actions builds preview
   - Comments on PR: "Preview build ready: QuickAi-1.2.0-pr7-abc1234-x64.zip"

3. **Maintainer reviews**:
   - Reviews code using security checklist
   - Downloads preview build from Actions
   - Tests locally
   - Approves PR

4. **Merge to dev**:
   - PR merged via "Squash and merge"
   - Dev branch updated
   - New preview build created: `QuickAi-1.2.0-dev-def5678-x64.zip`

5. **Community testing**:
   - Announce in discussions: "Please test dev preview build"
   - Users download and test
   - Report any bugs as issues

6. **After testing period (e.g., 1 week)**:
   - If stable: Maintainer merges `dev` → `master`
   - Creates tag `v1.2.0`
   - Production release published
   - Users get stable update

## Hotfix Process

For critical bugs in production:

```bash
# Create hotfix branch from master
git checkout master
git checkout -b hotfix/critical-bug

# Fix and commit
git commit -m "fix: critical security issue"

# Merge to master (fast-track)
git checkout master
git merge --no-ff hotfix/critical-bug
git tag v1.1.1
git push origin master --tags

# Also merge to dev to keep in sync
git checkout dev
git merge --no-ff hotfix/critical-bug
git push origin dev

# Delete hotfix branch
git branch -d hotfix/critical-bug
```

## Version Numbering

- **Major**: Breaking changes (v2.0.0)
- **Minor**: New features, backward compatible (v1.2.0)
- **Patch**: Bug fixes (v1.1.1)
- **Preview**: `-dev-shortsha` for dev builds
- **PR**: `-pr7-shortsha` for PR preview builds

## FAQ

**Q: Can I create PR directly to master?**
A: No, all PRs should target `dev` first for testing.

**Q: When should I merge dev to master?**
A: After thorough testing (1-2 weeks) and no critical issues.

**Q: Can I push directly to dev?**
A: Maintainers can, but PRs are preferred for code review.

**Q: How long do preview builds stay available?**
A: 14 days (GitHub Actions artifact retention), then deleted automatically.

**Q: Can I create a manual preview release?**
A: Yes, go to Actions → Build Dev Preview → Run workflow → Select branch.
