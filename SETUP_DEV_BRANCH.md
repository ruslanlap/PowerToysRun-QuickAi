# Setting Up Dev Branch Workflow

Quick guide for setting up the `dev` branch and GitFlow workflow.

## Step 1: Create Dev Branch

```bash
# Make sure you're on latest master
git checkout master
git pull origin master

# Create and push dev branch
git checkout -b dev
git push -u origin dev
```

## Step 2: Configure Branch Protection on GitHub

### Protect `master` branch:
1. Go to **Settings** → **Branches** → **Add rule**
2. Branch name pattern: `master`
3. Settings:
   - ✅ Require a pull request before merging
   - ✅ Require approvals: 1
   - ✅ Dismiss stale pull request approvals when new commits are pushed
   - ✅ Require status checks to pass before merging
   - ✅ Require branches to be up to date before merging
   - Status checks: Wait for workflows to run, then select:
     - `build (x64)`
     - `build (ARM64)`
   - ✅ Require conversation resolution before merging
   - ✅ Do not allow bypassing the above settings
4. **Save changes**

### Protect `dev` branch:
1. Go to **Settings** → **Branches** → **Add rule**
2. Branch name pattern: `dev`
3. Settings:
   - ✅ Require a pull request before merging
   - ✅ Require approvals: 1
   - ✅ Require status checks to pass before merging
   - Status checks: Select same as above
   - ✅ Require conversation resolution before merging
4. **Save changes**

## Step 3: Update GitHub PR Settings

1. Go to **Settings** → **General** → **Pull Requests**
2. Configure:
   - ✅ Allow squash merging (recommended for cleaner history)
   - Default to pull request title and description
   - ✅ Allow merge commits (for dev → master)
   - ❌ Allow rebase merging (optional)
   - ✅ Automatically delete head branches

## Step 4: Set Default Branch for PRs (Optional)

If you want external PRs to target `dev` by default:

1. Go to **Settings** → **General** → **Default branch**
2. Click "Switch to another branch"
3. Select `dev`
4. Confirm

**Note**: This means new PRs will default to `dev`, but you can still create PRs to `master` manually.

## Step 5: Update README.md

Add a section explaining the contribution workflow:

```markdown
## Contributing

We use a dev branch workflow:
- All PRs should target the `dev` branch
- After testing, changes are promoted to `master`
- Preview builds are available for testing

See [BRANCHING_STRATEGY.md](.github/BRANCHING_STRATEGY.md) for details.
```

## Step 6: Test the Workflow

### Test dev preview build:

```bash
# Create a test branch
git checkout dev
git checkout -b test/preview-workflow

# Make a small change
echo "# Test" >> TEST.md
git add TEST.md
git commit -m "test: verify dev preview workflow"
git push -u origin test/preview-workflow

# Create PR to dev branch on GitHub
# Verify that:
# 1. Build Dev Preview workflow runs
# 2. Artifacts are created
# 3. PR comment appears with download links
```

### Test production release:

```bash
# After testing dev branch
git checkout master
git merge --no-ff dev -m "Release v1.2.0-test"
git tag v1.2.0-test
git push origin master --tags

# Verify that:
# 1. Build and Release workflow runs
# 2. GitHub Release is created
# 3. Release has correct assets

# Clean up test release
git tag -d v1.2.0-test
git push origin :refs/tags/v1.2.0-test
```

## Step 7: Communicate to Contributors

Add to issue/PR templates:

**For `.github/PULL_REQUEST_TEMPLATE.md`:**

```markdown
## PR Checklist

- [ ] This PR targets the `dev` branch (not `master`)
- [ ] I have tested my changes locally
- [ ] I have added tests (if applicable)
- [ ] I have updated documentation (if needed)

**Note**: PRs are first merged to `dev` for preview testing, then promoted to `master` after verification.
```

## Step 8: Set Up Auto-Labeling (Optional)

Create `.github/labeler.yml`:

```yaml
preview-ready:
  - changed-files:
    - any-glob-to-any-file: '**/*'
    - base-branch: 'dev'

security-review-needed:
  - changed-files:
    - any-glob-to-any-file:
      - '**/*.xaml'
      - '**/Main.cs'
      - '**/*Window*.cs'
```

And workflow `.github/workflows/labeler.yml`:

```yaml
name: "Pull Request Labeler"
on:
  pull_request_target:
    types: [opened, synchronize]

jobs:
  label:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      pull-requests: write
    steps:
      - uses: actions/labeler@v5
```

## Troubleshooting

### Issue: Workflows not running on dev branch

**Solution**: Ensure `.github/workflows/build-dev-preview.yml` has:
```yaml
on:
  push:
    branches:
      - dev
```

### Issue: Can't push to protected branch

**Expected**: Protection is working correctly. Use PRs instead:
```bash
git checkout -b fix/my-fix
git push -u origin fix/my-fix
# Create PR on GitHub
```

### Issue: Status checks not showing

**Solution**:
1. Trigger at least one workflow run
2. Then they'll appear in branch protection settings
3. Go back and enable them

## Current PR Review Process

For the existing PR (streaming window feature):

```bash
# 1. Fetch PR branch (if it exists)
git fetch origin pull/ID/head:pr-streaming-window

# 2. Review code locally
git checkout pr-streaming-window

# 3. Check security using checklist
# See .github/PR_SECURITY_CHECKLIST.md

# 4. If approved, merge to dev
git checkout dev
git merge --no-ff pr-streaming-window -m "Merge PR: Add streaming results window"
git push origin dev

# 5. Test dev preview build
# Download from GitHub Actions
# Test locally

# 6. After 1-2 weeks of testing, promote to master
git checkout master
git merge --no-ff dev -m "Release v1.2.0: Streaming results window"
git tag v1.2.0
git push origin master --tags
```

## Summary

✅ Dev branch workflow protects master from untested changes
✅ Preview builds allow community testing before release
✅ Clear promotion path: feature → dev → master
✅ Automated builds on every PR
✅ Security review checklist for reviewers

---

**Next Steps:**
1. Run steps 1-6 above
2. Review existing PR with security checklist
3. Merge to dev (not master)
4. Test dev preview
5. Promote to master when ready
