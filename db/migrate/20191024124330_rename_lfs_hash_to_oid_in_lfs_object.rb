# frozen_string_literal: true

class RenameLfsHashToOidInLfsObject < ActiveRecord::Migration[5.2]
  def change
    rename_column :lfs_objects, :hash, :oid
  end
end
