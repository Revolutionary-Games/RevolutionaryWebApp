# frozen_string_literal: true

# View of single user's properties
class UserProperties < HyperComponent
  param :user, type: User
  param :showToken, default: false, type: Boolean
  param :showLFS, default: false, type: Boolean

  before_mount do
    @action_message = nil
    @action_running = false
  end

  render(DIV) do
    lookingAtSelf = @User == App.acting_user

    P { 'This is you' } if lookingAtSelf

    P { "email: #{@User.email}" }
    P { "name: #{@User.name}" }
    P { "admin: #{@User.admin}" }
    P { "developer: #{@User.developer}" }
    P { "sso_source: #{@User.sso_source}" }
    P { "local: #{@User.local}" }

    if App.acting_user&.admin?
      P { "session version: #{@User.session_version}" }
      P { "suspended: #{@User.suspended}" }
      P { "suspension reason: #{@User.suspended_reason}" }
      P { "is manual suspension: #{@User.suspended_manually}" }
    end

    hasToken = @User.has_api_token
    hasLFS = @User.has_lfs_token

    P { "api token: #{hasToken}" }

    if hasToken == 'true' && lookingAtSelf
      RS.Button(color: 'primary') { !@ShowToken ? 'View Token' : 'Hide Token' }.on(:click) {
        mutate @ShowToken = !@ShowToken
      }

      P { "Your token is: #{@User.api_token}" } if @ShowToken

      RS.Button(color: 'secondary') { 'Regenerate Token' }.on(:click) {
        GenerateAPITokenForUser.run
                               .then { mutate @ShowToken = true }
                               .fail { alert 'failed to run operation' }
      }

      RS.Button(color: 'secondary') { 'Clear Token' }.on(:click) {
        ResetAPITokenForUser.run(user_id: @User.id).then {
          @User.has_api_token!
          mutate @ShowToken = false
        }.fail { alert 'failed to run operation' }
      }

    elsif lookingAtSelf

      RS.Button(color: 'primary') { 'Generate API Token' }.on(:click) {
        GenerateAPITokenForUser.run.then {
          @User.has_api_token!
          mutate @ShowToken = true
        }.fail { alert 'failed to run operation' }
      }
    end

    P { "Git lfs token: #{hasLFS}" }

    if hasLFS == 'true' && lookingAtSelf
      RS.Button(color: 'primary') {
        !@ShowLFS ? 'View LFS Token' : 'Hide LFS Token'
      }.on(:click) {
        mutate @ShowLFS = !@ShowLFS
      }

      P { "Your Git LFS token is: #{@User.lfs_token}" } if @ShowLFS

      RS.Button(color: 'secondary') { 'Regenerate Token' }.on(:click) {
        GenerateLFSTokenForUser.run
                               .then { mutate @ShowLFS = true }
                               .fail { alert 'failed to run operation' }
      }

      RS.Button(color: 'secondary') { 'Clear Token' }.on(:click) {
        ResetLFSTokenForUser.run(user_id: @User.id).then {
          @User.has_lfs_token!
          mutate @ShowLFS = false
        }.fail { alert 'failed to run operation' }
      }

    elsif lookingAtSelf

      RS.Button(color: 'primary') { 'Generate Git LFS Token' }.on(:click) {
        GenerateLFSTokenForUser.run.then {
          @User.has_lfs_token!
          mutate @ShowLFS = true
        }.fail { alert 'failed to run operation' }
      }
    end

    BR {}
    H2 { 'Actions' }

    if @action_running
      DIV {
        RS.Spinner(color: 'primary')
        SPAN { 'Running action...' }
      }
    end

    P { @action_message } if @action_message

    RS.Button(color: lookingAtSelf ? 'warning' : 'danger') {
      if lookingAtSelf
        'Logout Everywhere'
      else
        'Force Logout'
      end
    }.on(:click) {
      start_action
      InvalidateUserSessions.run(user_id: @User.id).then {
        if lookingAtSelf
          # Redirect to login page
          # According to opal docs this should be right, but doesn't work...
          # Document.location.pathname = '/login'
          `document.location.href = '/login'`
        else
          action_finished 'Success'
        end
      }.fail { |error|
        action_finished "Failed to logout sessions: #{error}"
      }
    }

    unless lookingAtSelf
      BR {}
      RS.Button(color: 'danger') { 'Force Clear Tokens' }.on(:click) {
        start_action
        ResetLFSTokenForUser.run(user_id: @User.id).then {
          ResetAPITokenForUser.run(user_id: @User.id)
        }.then {
          @User.has_lfs_token!
          @User.has_api_token!
          action_finished 'Success'
        }.fail { |error|
          action_finished "Failed to run operation: #{error}"
        }
      }
    end
  end

  private

  def start_action
    mutate {
      @action_message = nil
      @action_running = true
    }
  end

  def action_finished(message)
    mutate {
      @action_message = message
      @action_running = false
    }
  end
end

# View of a single user
class UserView < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do
    user = User.find_by_id match.params[:id]

    unless user
      H1 { "No user exists with id = #{match.params[:id]}" }
      return
    end

    H1 { "User #{user.email} (id: #{user.id})" }

    H2 { 'Properties' }

    UserProperties(user: user)
  end
end
