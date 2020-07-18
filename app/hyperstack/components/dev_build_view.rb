# frozen_string_literal: true

# Shows a single devbuild
class DevBuildView < HyperComponent
  include Hyperstack::Router::Helpers

  before_mount do
    @build = DevBuild.find match.params[:id]
  end

  render(DIV) do
    unless @build
      H2 { 'No build found. Or you need to login to view it.' }
      return
    end

    H3 {
      "DevBuild (#{@build.id}) #{@build.build_hash} for #{@build.platform}"
    }
  end
end
