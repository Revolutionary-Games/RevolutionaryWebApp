# frozen_string_literal: true

# Single table line
class ReportItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :report
  render(TR) do
    TD { @Report.game_version.to_s }
    TH(scope: 'row') { Link("/report/#{@Report.id}") { @Report.id.to_s } }
    TD { Link("/report/#{@Report.id}") { @Report.updated_at.to_s } }
    TD { @Report.solved ? 'yes' : 'no' }
    # TD{"#{@Report.crash_time}"}
    TD { @Report.description.to_s }
    TD { @Report.public.to_s }
    TD { @Report.solved_comment.to_s }
  end
end

# A table of crash reports
class Reports < HyperComponent
  param :current_page, default: 0, type: Integer
  param :page_size, default: 25, type: Integer

  before_mount do
    @sort_by = :updated_at
    @order = :desc
    @show_solved = true
    @show_duplicates = true
  end

  def items
    scope = Report.visible_to(Hyperstack::Application.acting_user_id)

    scope = scope.not_solved unless @show_solved

    scope = scope.not_duplicate unless @show_duplicates

    # TODO: add search for word in the report

    if @sort_by == :updated_at && @order == :desc
      scope.index_by_updated_at
    elsif @sort_by == :updated_at && @order == :asc
      scope.index_by_updated_at_reverse
    elsif @sort_by == :id && @order == :asc
      scope
    elsif @sort_by == :id && @order == :desc
      scope.index_id_reverse
    else
      scope
    end
  end

  def list_management_components
    RS.Form(:inline) {
      RS.FormGroup(class: 'mb-2 mr-sm-4 mb-sm-0') {
        RS.Label(for: 'sortReportsBy', class: 'mr-sm-2') { 'sort by' }
        RS.Input(type: :select, id: 'sortReportsBy') {
          OPTION(value: '1') { 'Updated At' }
          OPTION(value: '2') { 'ID' }
        }.on(:change) { |e|
          mutate {
            @sort_by = if e.target.value == '1'
                         :updated_at
                       else
                         :id
                       end
          }
        }

        RS.Input(type: :select) {
          OPTION(value: '2') { 'Descending' }
          OPTION(value: '1') { 'Ascending' }
        }.on(:change) { |e|
          mutate {
            @order = if e.target.value == '1'
                       :asc
                     else
                       :desc
                     end
          }
        }
      }
      RS.FormGroup(class: 'mb-3 mr-sm-4 mb-sm-0') {
        RS.Label(:check, 'mr-sm-2') {
          RS.Input(type: :checkbox, checked: @show_solved) { ' ' }.on(:change) { |e|
            mutate @show_solved = e.target.checked
          }
          'show solved'
        }
      }
      RS.FormGroup(class: 'mb-2 mr-sm-4 mb-sm-0') {
        RS.Label(:check, 'mr-sm-2') {
          RS.Input(type: :checkbox, checked: @show_duplicates) { ' ' }.on(:change) { |e|
            mutate @show_duplicates = e.target.checked
          }
          'show duplicates'
        }
      }
    }
  end

  render(DIV) do
    H1 { 'Crash reports' }

    list_management_components

    BR {}

    Paginator(current_page: @CurrentPage,
              page_size: @PageSize,
              item_count: items.count,
              ref: set(:paginator)) {
      # This is set with a delay
      if @paginator
        RS.Table(:striped, :responsive) {
          THEAD {
            TR {
              TH { 'Version' }
              TH { 'ID' }
              TH { 'Updated At' }
              TH { 'Solved' }
              # TH{ "Crash Time" }
              TH { 'Description' }
              TH { 'Public' }
              TH { 'Solve comment' }
            }
          }

          TBODY {
            items.offset(@paginator.offset).take(@paginator.take_count).each { |report|
              ReportItem(report: report)
            }
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate @CurrentPage = page
    }
  end
end
