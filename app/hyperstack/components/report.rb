# frozen_string_literal: true

# Shows a single report information
class ReportView < HyperComponent
  include Hyperstack::Router::Helpers
  param :show_make_duplicate_of, default: false, type: Boolean
  param :duplicate_of_id, default: '', type: String
  param :duplicate_error, default: nil, type: String

  def apply_make_duplicate_of
    target = nil
    begin
      target = Integer(@duplicate_of_id)
    rescue StandardError
      mutate @duplicate_error = "Invalid id given: '#{@duplicate_of_id}'"
      return
    end

    UpdateReportDuplicateStatus.run(report_id: @report.id, duplicate_of_id: target).then {
      mutate {
        @show_make_duplicate_of = false
        @duplicate_of_id = ''
        @duplicate_error = 'Duplicate status updated'
      }
    }.fail { |error|
      mutate @duplicate_error = "Error when updating report: #{error}"
    }
  end

  def clear_duplicate_status
    UpdateReportDuplicateStatus.run(report_id: @report.id, duplicate_of_id: nil).then {
      mutate {
        @show_make_duplicate_of = false
        @duplicate_of_id = ''
        @duplicate_error = 'Duplicate status updated'
      }
    }.fail { |error|
      mutate @duplicate_error = "Error when updating report: #{error}"
    }
  end

  render(DIV) do
    @report = Report.find_by_id match.params[:id]

    unless @report
      H1 { "No report exists with id = #{match.params[:id]}" }
      return
    end

    H1 { "Report #{@report.id}" }

    # H1 { "You are not allowed to view this report or it doesn't exist"} if !report.????

    UL {
      LI { "Game Version: #{@report.game_version}" }
      LI { 'Public: ' + (@report.public ? 'yes' : 'no') }
      LI { "Updated At: #{@report.updated_at}" }
      LI { "Created At: #{@report.created_at}" }
      LI { "Crash Time: #{@report.crash_time}" }
      LI { 'Solved: ' + (@report.solved ? 'yes' : 'no') }
      LI { "Solve comment: #{@report.solved_comment}" }
      LI { "Reporter IP: #{@report.reporter_ip}" } if App.acting_user&.developer?
      LI { "Reporter Email: #{@report.reporter_email}" } if App.acting_user&.admin?
      LI {
        SPAN { 'Duplicate of: ' }

        unless @report.duplicate_of_id.nil?
          Link("/report/#{@report.duplicate_of_id}") { "Report #{@report.duplicate_of_id}" }
          SPAN { ' ' }
          BUTTON { 'Clear duplicate status' }.on(:click) {
            clear_duplicate_status
          }
        end

        P { @duplicate_error } if @duplicate_error

        if @report.duplicate_of_id.nil?
          if @show_make_duplicate_of
            BR {}
            duplicate_input = INPUT(placeholder: 'id of report this is a duplicate of',
                                    value: @duplicate_of_id)

            duplicate_input.on(:enter) do |_event|
              apply_make_duplicate_of
            end
            duplicate_input.on(:change) do |event|
              mutate @duplicate_of_id = event.target.value
            end

            BUTTON(disabled: @duplicate_of_id.nil? || @duplicate_of_id.empty?) { 'Apply' }
              .on(:click) {
              apply_make_duplicate_of
            }
            BUTTON { 'Cancel' }.on(:click) {
              mutate {
                @show_make_duplicate_of = false
                @duplicate_of_id = ''
              }
            }
          elsif App.acting_user&.developer?
            BUTTON { 'Mark as duplicate' }.on(:click) {
              mutate @show_make_duplicate_of = true
            }
          end
        end
      }
    }

    if !@report.duplicates.nil? && !@report.duplicates.empty?
      H2 { 'Duplicates of this report' }

      UL {
        @report.duplicates.each { |duplicate|
          LI { Link("/report/#{duplicate.id}") { duplicate.id } }
        }
      }
    end

    H2 { 'Description' }
    P { @report.description }
    P { @report.extra_description }

    H2 { 'Notes' }
    P { PRE { @report.notes } }

    H2 { 'Callstack' }
    P { PRE { @report.primary_callstack } }

    if App.acting_user&.developer?
      H2 { 'Log files' }
      P { PRE { @report.log_files } }
    end

    H2 { 'Full dump' }
    P { PRE { @report.processed_dump } }
  end
end
