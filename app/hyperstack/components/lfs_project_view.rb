# frozen_string_literal: true

# Shows a single LFS project
class LfsProjectView < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do
    project = LfsProject.find_by slug: match.params[:slug]

    unless project
      H1 { 'No project found' }
      return
    end

    H1 { "Git LFS Project #{project.name}" }

    UL {
      LI { 'Public: ' + (project.public ? 'yes' : 'no') }
      LI { "Updated At: #{project.updated_at}" }
      LI { "Created At: #{project.created_at}" }
      LI {
        SPAN { 'Git LFS URL: ' }
        A(href: project.lfs_url) { project.lfs_url }
      }
    }

    P { 'Visit your profile to find your LFS access token.' }

    H2 { 'Statistics' }

    RS.Table(:bordered) {
      TBODY {
        TR {
          TH { 'Total size (MiB)' }
          TD { project.total_size_mib.to_s }
        }

        TR {
          TH { 'Item count' }
          TD { (project.total_object_count || 0).to_s }
        }
      }
    }

    P { "Statistics updated: #{project.total_size_updated || 'never'}" }

    H2 { 'Files' }
    P { "File tree generated at: #{project.file_tree_updated || 'never'}" }
    P { 'TODO: some kind of list' }
  end
end
