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
  end

  def check_description(description)
    @description_format_error = nil

    description.each_line{|line|
      if line.length > NICE_DEV_BUILD_DESCRIPTION_WIDTH
        @description_format_error = "Description contains a too long line: " + line
        break
      end
    }
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

    UL {
      LI { "Hash: #{@build.build_hash}" }
      LI { "Platform: #{@build.platform}" }
      LI { "Build of the Day (BOTD): #{@build.build_of_the_day}" }
      LI { "Branch (reported by uploader): #{@build.branch}" }
      LI { "Verified: #{@build.verified} by: #{@build.user&.name || 'no one'}" }
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
      if !@new_description.blank? && @new_description.length < 20
        can_save = false
      end

      # Can't save a blank description if this is a botd
      if @build.build_of_the_day && @new_description.blank?
        can_save = false
      end

      if @description_format_error
        can_save = false
      end

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
      RS.Button(color: 'primary', disabled: @build.description.blank?) {
        'Promote to BOTD'
      } .on(:click) {
        handle_action BuildOps::MakeBuildOfTheDay.run(build_id: @build.id)
      }
    else
      if App.acting_user.admin?
        RS.Button(color: 'warning') { 'Remove BOTD Status (on all builds)' }.on(:click) {
          handle_action BuildOps::ClearBuildOfTheDay.run
        }
      else
        SPAN { 'Already BOTD' }
      end
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
