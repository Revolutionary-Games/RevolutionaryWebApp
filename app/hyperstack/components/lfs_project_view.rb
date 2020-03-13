# frozen_string_literal: true

# Table row
class LFSObjectItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :object
  render(TR) do
    TH(scope: 'row') { @Object.oid.to_s }
    TD { @Object.size_mib.to_s + ' MiB' }
    TD { @Object.created_at.to_s }
  end
end

# Shows a single LFS project
class LfsProjectView < HyperComponent
  include Hyperstack::Router::Helpers

  param :current_raw_page, default: 0, type: Integer

  def raw_items
    LfsObject.by_project(@project.id).sort_by_created_at
  end

  before_mount do
    @show_raw_objects = false
    @project = LfsProject.find_by slug: match.params[:slug]
  end

  render(DIV) do
    unless @project
      H1 { 'No project found' }
      return
    end

    H1 { "Git LFS Project #{@project.name}" }

    UL {
      LI { 'Public: ' + (@project.public ? 'yes' : 'no') }
      LI { "Updated At: #{@project.updated_at}" }
      LI { "Created At: #{@project.created_at}" }
      LI {
        SPAN { 'Git LFS URL: ' }
        A(href: @project.lfs_url) { @project.lfs_url }
      }
    }

    P { 'Visit your profile to find your LFS access token.' }

    H2 { 'Statistics' }

    RS.Table(:bordered) {
      TBODY {
        TR {
          TH { 'Total size (MiB)' }
          TD { @project.total_size_mib.to_s }
        }

        TR {
          TH { 'Item count' }
          TD { (@project.total_object_count || 0).to_s }
        }
      }
    }

    P { "Statistics updated: #{@project.total_size_updated || 'never'}" }

    if App.acting_user&.developer?
      H3 { 'Raw Objects' }

      if @show_raw_objects
        Paginator(current_page: @CurrentRawPage,
                  item_count: raw_items.count,
                  show_totals: true,
                  ref: set(:paginator)) {
          # This is set with a delay
          if @paginator
            RS.Table(:striped, :reactive) {
              THEAD {
                TR {
                  TH { 'OID' }
                  TH { 'Size' }
                  TH { 'Created At' }
                }
              }

              TBODY {
                raw_items.offset(@paginator.offset).take(@paginator.take_count).each { |object|
                  LFSObjectItem(object: object)
                }
              }
            }
          end
        }.on(:page_changed) { |page|
          mutate @CurrentRawPage = page
        }
      else
        RS.Button(color: 'secondary') {
          'View raw Git LFS objects'
        }.on(:click) {
          mutate @show_raw_objects = true
        }
      end

      BR {}
      BR {}
    end

    H2 { 'Files' }
    P { "File tree generated at: #{@project.file_tree_updated || 'never'}" }
    P { 'TODO: some kind of list' }
  end
end
