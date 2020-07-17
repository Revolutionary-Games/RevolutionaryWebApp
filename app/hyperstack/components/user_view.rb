# frozen_string_literal: true

# View of single user's properties
class UserProperties < HyperComponent
  param :user, type: User
  param :showToken, default: false, type: Boolean
  param :showLFS, default: false, type: Boolean

  before_mount do
    @action_message = nil
    @action_running = false

    @new_suspend_message = ''
    @editing_suspend = false

    @show_launcher_link = false
    @unlink_in_progress = false
    @generate_link_in_progress = false
    @link_operation_result = nil
  end

  render(DIV) do
    lookingAtSelf = @User == App.acting_user
    hasToken = @User.has_api_token
    hasLFS = @User.has_lfs_token

    P { 'This is you' } if lookingAtSelf

    UL {
      LI { "email: #{@User.email}" }
      LI { "name: #{@User.name}" }
      LI { "admin: #{@User.admin}" }
      LI { "developer: #{@User.developer}" }
      LI { "sso_source: #{@User.sso_source}" }
      LI { "local: #{@User.local}" }

      if App.acting_user&.admin?
        LI { "session version: #{@User.session_version}" }
        LI { SPAN { "suspended: #{@User.suspended}" } }
        LI { "suspension reason: #{@User.suspended_reason}" }
        LI { "is manual suspension: #{@User.suspended_manually}" }
        LI { "Total times launcher linked: #{@User.total_launcher_links}" }
      end

      LI { "API token: #{hasToken}" }

      if hasToken == 'true' && lookingAtSelf
        RS.Button(color: 'primary') { !@ShowToken ? 'View Token' : 'Hide Token' }.on(:click) {
          mutate @ShowToken = !@ShowToken
        }

        P { "Your token is: #{@User.api_token}" } if @ShowToken

        RS.Button(color: 'secondary') { 'Regenerate Token' }.on(:click) {
          UserOps::GenerateAPITokenForUser.run
                                          .then { mutate @ShowToken = true }
                                          .fail { alert 'failed to run operation' }
        }

        RS.Button(color: 'secondary') { 'Clear Token' }.on(:click) {
          UserOps::ResetAPITokenForUser.run(user_id: @User.id).then {
            @User.has_api_token!
            mutate @ShowToken = false
          }.fail { alert 'failed to run operation' }
        }

      elsif lookingAtSelf

        RS.Button(color: 'primary') { 'Generate API Token' }.on(:click) {
          UserOps::GenerateAPITokenForUser.run.then {
            @User.has_api_token!
            mutate @ShowToken = true
          }.fail { alert 'failed to run operation' }
        }
      end

      LI { "Git lfs token: #{hasLFS}" }

      if hasLFS == 'true' && lookingAtSelf
        RS.Button(color: 'primary') {
          !@ShowLFS ? 'View LFS Token' : 'Hide LFS Token'
        }.on(:click) {
          mutate @ShowLFS = !@ShowLFS
        }

        P { "Your Git LFS token is: #{@User.lfs_token}" } if @ShowLFS

        RS.Button(color: 'secondary') { 'Regenerate Token' }.on(:click) {
          UserOps::GenerateLFSTokenForUser.run
                                          .then { mutate @ShowLFS = true }
                                          .fail { alert 'failed to run operation' }
        }

        RS.Button(color: 'secondary') { 'Clear Token' }.on(:click) {
          UserOps::ResetLFSTokenForUser.run(user_id: @User.id).then {
            @User.has_lfs_token!
            mutate @ShowLFS = false
          }.fail { alert 'failed to run operation' }
        }

      elsif lookingAtSelf

        RS.Button(color: 'primary') { 'Generate Git LFS Token' }.on(:click) {
          UserOps::GenerateLFSTokenForUser.run.then {
            @User.has_lfs_token!
            mutate @ShowLFS = true
          }.fail { alert 'failed to run operation' }
        }
      end
    }

    BR {}

    H2 { 'Launcher Links' }
    P {
      "You can link up to #{DEFAULT_MAX_LAUNCHER_LINKS} Thrive Launchers to your account " \
      'to access dev center features from the launcher. By linking a launcher you can access' \
      ' devbuilds.'
    }

    UL {
      @User.launcher_links.each { |link|
        used = link.last_connection || 'never'
        LI {
          SPAN {
            "Created at: #{link.created_at}, last used from: #{link.last_ip} at: " \
               "#{used}, total uses: #{link.total_api_calls}"
          }
          RS.Button(color: 'danger', class: 'LeftMargin') {
            SPAN { 'Unlink' }
          }.on(:click) {
            mutate {
              @link_operation_result = nil
            }
            UserOps::DeleteLauncherLink.run(link_id: link.id).then {
              mutate {
                @link_operation_result = 'Link deleted'
              }
            }.fail { |error|
              mutate @link_operation_result = "Failed to delete link: #{error}"
            }
          }
        }
      }

      LI { 'No links configured' } if @User.launcher_links.count < 1
    }

    if @show_launcher_link
      BR {}
      P { "Input this code in the launcher: #{@User.launcher_link_code}" }
      P { "The code will expire: #{@User.launcher_code_expires}" }
      BR {}
    end

    P { @link_operation_result.to_s } if @link_operation_result

    if lookingAtSelf
      RS.Button(color: 'success') {
        SPAN { 'Link Launcher' }
        RS.Spinner(size: 'sm') if @generate_link_in_progress
      }.on(:click) {
        mutate {
          @show_launcher_link = false
          @generate_link_in_progress = true
          @link_operation_result = nil
        }

        UserOps::CreateNewLauncherLinkCodeForUser.run(user_id: @User.id).then {
          mutate {
            @show_launcher_link = true
            @generate_link_in_progress = false
            # Force refresh this stuff
            @User.launcher_link_code!
            @User.launcher_code_expires!
          }
        }.fail { |error|
          mutate {
            @generate_link_in_progress = false
            @link_operation_result = "Failed to start launcher linking: #{error}"
          }
        }
      }
    end

    RS.Button(color: 'danger', class: 'LeftMargin') {
      SPAN { 'Unlink All Launchers' }
      RS.Spinner(size: 'sm') if @unlink_in_progress
    }.on(:click) {
      mutate {
        @unlink_in_progress = true
        @link_operation_result = ''
      }
      UserOps::ClearAllLauncherLinksForUser.run(user_id: @User.id).then {
        mutate {
          @unlink_in_progress = false
          @link_operation_result = 'Successfully deleted'
        }
      }.fail { |error|
        mutate {
          @unlink_in_progress = false
          @link_operation_result = "Failed to clear launchers: #{error}"
        }
      }
    }

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
      UserOps::InvalidateUserSessions.run(user_id: @User.id).then {
        if lookingAtSelf
          # Redirect to login page
          Window.location.path = '/login'
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
        UserOps::ResetLFSTokenForUser.run(user_id: @User.id).then {
          UserOps::ResetAPITokenForUser.run(user_id: @User.id)
        }.then {
          @User.has_lfs_token!
          @User.has_api_token!
          action_finished 'Success'
        }.fail { |error|
          action_finished "Failed to run operation: #{error}"
        }
      }

      BR {}

      if @editing_suspend
        RS.Input(type: :text, value: @new_suspend_message,
                 placeholder: 'Enter suspension reason').on(:change) { |e|
          mutate @new_suspend_message = e.target.value
        }

        RS.Button(color: 'secondary') { 'Cancel' }.on(:click) {
          mutate @editing_suspend = false
        }

        RS.Button(color: 'primary', disabled: @new_suspend_message.blank?) {
          'Suspend'
        }.on(:click) {
          # The start_action will call mutate
          @editing_suspend = false
          start_action
          UserOps::SuspendUser.run(user_id: @User.id, reason: @new_suspend_message).then {
            action_finished 'Success'
          }.fail { |error|
            action_finished "Failed to suspend user: #{error}"
          }
        }
      else
        if @User.suspended
          RS.Button(color: 'warning') { 'Unsuspend' }.on(:click) {
            start_action
            UserOps::UnSuspendUser.run(user_id: @User.id).then {
              action_finished 'Success'
            }.fail { |error|
              action_finished "Failed to unsuspend user: #{error}"
            }
          }
        else
          RS.Button(color: 'danger') { 'Suspend' }.on(:click) {
            mutate {
              @editing_suspend = true
              @new_suspend_message = @User.suspended_reason || ''
            }
          }
        end
      end

      BR {}

      RS.Button(color: 'danger') { 'Unlink All Launchers' }.on(:click) {
        start_action
        UserOps::ClearAllLauncherLinksForUser.run(user_id: @User.id).then {
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
