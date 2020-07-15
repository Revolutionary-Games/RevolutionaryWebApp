class AddBuildZipHashToDevBuild < ActiveRecord::Migration[5.2]
  def change
    add_column :dev_builds, :build_zip_hash, :string
  end
end
