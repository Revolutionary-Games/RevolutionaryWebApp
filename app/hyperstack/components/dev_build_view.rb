# frozen_string_literal: true

NICE_DEV_BUILD_DESCRIPTION_WIDTH = 70

# Shows a single devbuild
class DevBuildView < HyperComponent
  include Hyperstack::Router::Helpers

  before_mount do
    @build = DevBuild.find match.params[:id]

    @action_running = false
    @action_message = nil

    @editing_description = false
    @new_description = ''
    @description_being_updated = false
    @description_update_error = nil
    @description_format_error = nil

    @verifying_build = false
    @verify_sibling_build = true
    @verified_check_box = false
  end

  def check_description(description)
    @description_format_error = nil

    description.each_line { |line|
      if line.length > NICE_DEV_BUILD_DESCRIPTION_WIDTH
        @description_format_error = 'Description contains a too long line: ' + line
        break
      end
    }
  end

  def refresh_build_user
    mutate @build.user!
  end

  def refresh_bodt
    mutate @build.build_of_the_day!
  end

  def unverified_build_actions
    if !@verifying_build
      RS.Button(color: 'primary') {
        'Verify This Build'
      } .on(:click) {
        mutate {
          @verified_check_box = false
          @verifying_build = true
        }
      }
    else
      RS.Form {
        P {
          'Build verification is meant as an extra safety precaution against anonymously ' \
            'uploaded builds. Non-anonymous builds are authenticated with a key that the ' \
            'build server has. This means that they are safe as long as our repository ' \
            "isn't compromised, by for example a developer's account being hacked. Because" \
            'anonymous builds are not verified by a key, anyone else who knows the approach ' \
            'could upload their own files. As such before the launcher downloads an ' \
            'anonymous build without complaint it needs to be verified.'
        }
        P {
          'Before marking this build as verified, please at least do the following: ' \
            'check that a pull request (PR) exists on our Github with the latest commit ' \
            "hash matching, that PR doesn't touch any of the upload and package scripts, the " \
            "PR doesn't contains suspicious C# code in it (that might be a virus), and " \
            'that the build results on CircleCI mention in the upload builds step that a ' \
            'build with the expected hash was uploaded. These checks should ensure that the ' \
            "build doesn't have unexpected code in it"
        }
        RS.FormGroup(check: true) {
          RS.Input(type: :checkbox, checked: @verify_sibling_build, id: 'siblingCheck').on(
            :change
          ) { |e|
            mutate {
              @verify_sibling_build = e.target.checked
            }
          }
          RS.Label(check: true, for: 'siblingCheck') {
            'Verify sibling builds (the same hash but different platforms)'
          }
        }
        RS.FormGroup(check: true) {
          RS.Input(type: :checkbox, checked: @verified_check_box, id: 'verifiedCheck').on(
            :change
          ) { |e|
            mutate {
              @verified_check_box = e.target.checked
            }
          }
          RS.Label(check: true, for: 'verifiedCheck') {
            'I have verified this build according to the instructions'
          }
        }

        BR {}

        RS.FormGroup {
          RS.Button(color: 'primary', disabled: !@verified_check_box) { 'Verify It' }.on(
            :click
          ) { |e|
            e.prevent_default
            mutate @verifying_build = false

            handle_action(BuildOps::VerifyBuild.run(
                            build_id: @build.id, verify_sibling: @verify_sibling_build
                          )).then {
              refresh_build_user
            }
          }

          RS.Button(class: 'LeftMargin') { 'Cancel' }.on(:click) { |e|
            e.prevent_default
            mutate @verifying_build = false
          }
        }
      }
    end
  end

  def verified_build_actions
    if @build.build_of_the_day
      SPAN { 'This is a BOTD' }
    elsif !@verifying_build
      RS.Button(color: 'warning') { 'Unverify Build' }.on(:click) {
        mutate {
          @verified_check_box = false
          @verifying_build = true
        }
      }
    else
      RS.Form {
        P {
          'Builds should only be unverified if a mistake was made when verifying ' \
            'or turns out that the build contained well hidden malicious code'
        }
        RS.FormGroup(check: true) {
          RS.Input(type: :checkbox, checked: @verify_sibling_build, id: 'siblingCheck').on(
            :change
          ) { |e|
            mutate {
              @verify_sibling_build = e.target.checked
            }
          }
          RS.Label(check: true, for: 'siblingCheck') {
            'Unverify sibling builds'
          }
        }
        RS.FormGroup(check: true) {
          RS.Input(type: :checkbox, checked: @verified_check_box, id: 'verifiedCheck').on(
            :change
          ) { |e|
            mutate {
              @verified_check_box = e.target.checked
            }
          }
          RS.Label(check: true, for: 'verifiedCheck') {
            'I want to unverify this build'
          }
        }
        BR {}

        RS.FormGroup {
          RS.Button(color: 'danger', disabled: !@verified_check_box) {
            'Remove Verification'
          } .on(:click) { |e|
            e.prevent_default
            mutate @verifying_build = false

            handle_action(BuildOps::UnVerifyBuild.run(
                            build_id: @build.id, verify_sibling: @verify_sibling_build
                          )).then {
              refresh_build_user
            }
          }

          RS.Button(class: 'LeftMargin') { 'Cancel' }.on(:click) { |e|
            e.prevent_default
            mutate @verifying_build = false
          }
        }
      }
    end
  end

  render(DIV) do
    unless App.acting_user
      H2 { 'Please login to view DevBuilds' }
      return
    end

    unless @build
      H2 { 'No build found. Or you need to login to view it.' }
      return
    end

    H3 {
      "DevBuild (#{@build.id}) #{@build.build_hash} for #{@build.platform}"
    }

    H4 { 'Properties' }

    verifier = 'no one'

    if @build.user
      verifier = if @build.user.name
                   @build.user&.name
                 else
                   "user (#{@build.user.id})"
                 end
    end

    UL {
      LI { "Hash: #{@build.build_hash}" }
      LI { "Platform: #{@build.platform}" }
      LI { "Build of the Day (BOTD): #{@build.build_of_the_day}" }
      LI { "Branch (reported by uploader): #{@build.branch}" }
      LI { "Verified: #{@build.verified} by: #{verifier}" }
      LI { "Anonymous: #{@build.anonymous}" }
      LI { "Important: #{@build.important}" }
      LI { "Keep: #{@build.keep}" }
      LI { "Downloads: #{@build.downloads}" }
      LI { "Score: #{@build.score}" }
      LI { "Related PR (detected by hash, may not be always accurate): #{@build.pr_url}" }
      LI { "Created: #{@build.created_at}" }
      LI { "Updated: #{@build.updated_at}" }
    }

    H4 { 'Description' }

    if !@editing_description
      PRE { @build.description }
    else
      RS.Input(type: :textarea, value: @new_description,
               placeholder: 'Build description').on(:change) { |e|
        mutate {
          @new_description = e.target.value
          check_description @new_description
        }
      }
    end

    BR {}

    return unless App.acting_user&.developer?

    if !@editing_description
      RS.Button(color: 'secondary') { 'Edit' }.on(:click) {
        mutate {
          @editing_description = true
          @description_update_error = nil
          @description_being_updated = false
          @new_description = @build.description
          check_description @new_description
        }
      }
    else

      P { @description_format_error } if @description_format_error

      P { @description_update_error } if @description_update_error

      if @description_being_updated
        DIV {
          RS.Spinner(color: 'primary')
          SPAN { 'Updating description' }
        }
      end

      can_save = true
      # Can't save if has typed in something and that's less than 20 characters
      can_save = false if !@new_description.blank? && @new_description.length < 20

      # Can't save a blank description if this is a botd
      can_save = false if @build.build_of_the_day && @new_description.blank?

      can_save = false if @description_format_error

      RS.Button(color: 'primary', disabled: !can_save) { 'Save' }.on(:click) {
        mutate {
          @description_being_updated = true
          @description_update_error = nil
        }
        BuildOps::UpdateDescription.run(build_id: @build.id,
                                        description: @new_description).then {
          mutate {
            @description_being_updated = false
            @editing_description = false
            @build.description!
          }
        }.fail { |error|
          mutate {
            @description_being_updated = false
            @description_update_error = "Failed to save: #{error}"
          }
        }
      }
      RS.Button(color: 'danger', class: 'LeftMargin') { 'Cancel' }.on(:click) {
        mutate {
          @editing_description = false
        }
      }
    end

    BR {}
    BR {}
    H4 { 'Actions' }

    P { @action_message } if @action_message

    if @action_running
      DIV {
        RS.Spinner(color: 'primary')
        SPAN { 'Running action...' }
      }
    end

    if !@build.build_of_the_day
      can_make_bodt = true

      if @build.description.blank?
        can_make_bodt = false
      end

      if !@build.verified && @build.anonymous
        can_make_bodt = false
      end

      RS.Button(color: 'primary', disabled: !can_make_bodt) {
        'Promote to BOTD'
      } .on(:click) {
        handle_action(BuildOps::MakeBuildOfTheDay.run(build_id: @build.id)).then{
          refresh_bodt
        }
      }
    else
      if App.acting_user.admin?
        RS.Button(color: 'warning') { 'Remove BOTD Status (on all builds)' }.on(:click) {
          handle_action(BuildOps::ClearBuildOfTheDay.run).then{
            refresh_bodt
          }
        }
      else
        SPAN { 'Already BOTD' }
      end
    end

    BR {}
    BR {}

    if !@build.verified
      unverified_build_actions
    else
      verified_build_actions
    end

    BR {}
    HR {}
    can_delete = !@build.build_of_the_day && (App.acting_user.admin? || (!@build.keep &&
      !@build.important))

    RS.Button(color: 'danger', disabled: !can_delete) { 'Delete' }.on(:click) {
      handle_action BuildOps::DeleteBuild.run(build_id: @build.id)
    }
  end

  private

  def handle_action(promise)
    start_action

    promise.then {
      end_action 'Success'
    }.fail { |error|
      end_action "Failed to run action: #{error}"
    }
  end

  def start_action
    mutate {
      @action_running = true
      @action_message = nil
    }
  end

  def end_action(message)
    mutate {
      @action_running = false
      @action_message = message
    }
  end
end
